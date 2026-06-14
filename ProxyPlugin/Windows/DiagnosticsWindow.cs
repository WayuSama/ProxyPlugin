using System;
using System.Diagnostics;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ProxyPlugin.Core;
using ProxyPlugin.Localization;

namespace ProxyPlugin.Windows;

/// <summary>
/// Connectivity diagnostics: probes the key services the game and plugins rely on along two routes
/// side by side — a genuine direct (no-proxy) connection and the plugin's live route through
/// <see cref="DynamicProxy"/> — so the user can see at a glance which endpoints need the proxy and
/// whether the proxy actually helps. Probes run off the render thread and are marked
/// <see cref="HttpInterceptor.NoRewriteKey"/> so the interceptor never rewrites, injects, or logs them.
/// </summary>
public sealed class DiagnosticsWindow : Window, IDisposable
{
    private static readonly Uri SampleDestination = new("https://github.com/");

    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(1.00f, 0.78f, 0.25f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.40f, 0.35f, 1f);
    private static readonly Vector4 Grey = new(0.60f, 0.60f, 0.60f, 1f);

    private enum ProbeState { Idle, Testing, Ok, Reachable, Failed }

    private enum Route { Direct, Proxy }

    // Immutable result snapshot, swapped atomically so the render thread never reads a half-written row.
    private sealed class ProbeResult
    {
        public ProbeState State { get; init; }
        public int StatusCode { get; init; }
        public long ElapsedMs { get; init; }
        public string Detail { get; init; } = "";
    }

    private sealed class Target
    {
        private volatile ProbeResult _direct = new();
        private volatile ProbeResult _proxy = new();

        public Target(string categoryKey, string nameKey, string url)
        {
            CategoryKey = categoryKey;
            NameKey = nameKey;
            Url = url;
        }

        public string CategoryKey { get; }
        public string NameKey { get; }
        public string Url { get; }

        public ProbeResult Get(Route route) => route == Route.Direct ? _direct : _proxy;

        public void Set(Route route, ProbeResult result)
        {
            if (route == Route.Direct)
                _direct = result;
            else
                _proxy = result;
        }
    }

    private readonly Configuration _config;
    private readonly DynamicProxy _proxy;
    private readonly HttpClient _directClient;
    private readonly HttpClient _proxyClient;
    private readonly CancellationTokenSource _cts = new();
    private readonly Target[] _targets;

    public DiagnosticsWindow(Configuration config, DynamicProxy proxy)
        : base("Network Diagnostics###ProxyPluginDiagnostics")
    {
        _config = config;
        _proxy = proxy;
        _targets = BuildTargets();

        // Two legs: a genuine direct connection and one through the live DynamicProxy. Probes carry
        // NoRewriteKey (see RunProbe) so the interceptor never re-injects the proxy into the direct leg.
        _directClient = CreateProbeClient(null);
        _proxyClient = CreateProbeClient(_proxy);

        Size = new Vector2(600, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // A null proxy yields a direct (no-proxy) client; otherwise route through the given proxy.
    private static HttpClient CreateProbeClient(DynamicProxy? proxy)
    {
        var client = new HttpClient(
            new SocketsHttpHandler { UseProxy = proxy != null, Proxy = proxy },
            disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProxyPlugin-Diagnostics/1.0");
        return client;
    }

    private static Target[] BuildTargets() => new[]
    {
        // Baseline connectivity: gstatic works inside mainland China without a proxy; Google does not,
        // so a Google success effectively confirms the proxy reaches the open internet.
        new Target("diag.cat.basic", "diag.t.basic", "https://www.gstatic.com/generate_204"),
        new Target("diag.cat.basic", "diag.t.google", "https://www.google.com/generate_204"),

        // Dalamud / plugin distribution (XIVLauncher CN uses the ottercorp server).
        new Target("diag.cat.dalamud", "diag.t.ottercorp", "https://aonyx.ffxiv.wang"),

        // GitHub surfaces hit during plugin install/update and asset downloads.
        new Target("diag.cat.github", "diag.t.ghMain", "https://github.com"),
        new Target("diag.cat.github", "diag.t.ghApi", "https://api.github.com"),
        new Target("diag.cat.github", "diag.t.ghRaw", "https://raw.githubusercontent.com"),
        new Target("diag.cat.github", "diag.t.ghObjects", "https://objects.githubusercontent.com"),
        new Target("diag.cat.github", "diag.t.ghCodeload", "https://codeload.github.com"),

        // Game-data and localization services used by the wider plugin ecosystem.
        new Target("diag.cat.services", "diag.t.universalis", "https://universalis.app"),
        new Target("diag.cat.services", "diag.t.xivapi", "https://xivapi.com"),
        new Target("diag.cat.services", "diag.t.crowdin", "https://api.crowdin.com"),
    };

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        _directClient.Dispose();
        _proxyClient.Dispose();
        _cts.Dispose();
    }

    // Auto-probe whenever the window is opened so the data is fresh without an extra click.
    public override void OnOpen() => RunAll();

    public override void Draw()
    {
        // Keep the title localized while preserving a stable ImGui id (the part after ###).
        WindowName = Loc.T("diag.title") + "###ProxyPluginDiagnostics";

        ImGui.TextWrapped(Loc.T("diag.intro"));
        ImGui.TextColored(Grey, Loc.T("diag.mode", DescribeRoute()));
        ImGui.TextColored(Grey, Loc.T("diag.legend"));
        ImGui.Separator();

        if (ImGui.Button(Loc.T("diag.runAll")))
            RunAll();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("diag.copy")))
            CopyReport();

        ImGui.Spacing();
        DrawTable();
    }

    private void DrawTable()
    {
        if (!ImGui.BeginTable("##diag", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn(Loc.T("diag.col.target"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("diag.col.direct"), ImGuiTableColumnFlags.WidthFixed, 132);
        ImGui.TableSetupColumn(Loc.T("diag.col.proxy"), ImGuiTableColumnFlags.WidthFixed, 132);
        ImGui.TableSetupColumn("##act", ImGuiTableColumnFlags.WidthFixed, 52);
        ImGui.TableHeadersRow();

        string? lastCategory = null;
        for (int i = 0; i < _targets.Length; i++)
        {
            var t = _targets[i];
            if (t.CategoryKey != lastCategory)
            {
                lastCategory = t.CategoryKey;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(Grey, Loc.T(t.CategoryKey));
            }

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Loc.T(t.NameKey));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(t.Url);

            ImGui.TableNextColumn();
            DrawRouteCell(t.Get(Route.Direct));

            ImGui.TableNextColumn();
            DrawRouteCell(t.Get(Route.Proxy));

            ImGui.TableNextColumn();
            // Index suffix keeps each button's ImGui id unique while sharing the "Test" label.
            if (ImGui.Button(Loc.T("test") + $"##t{i}"))
                RunBoth(t);
        }

        ImGui.EndTable();
    }

    private static void DrawRouteCell(ProbeResult r)
    {
        switch (r.State)
        {
            case ProbeState.Testing:
                ImGui.TextColored(Grey, Loc.T("test.testing"));
                break;
            case ProbeState.Ok:
            case ProbeState.Reachable:
                // Any HTTP response means the path works; a 4xx/5xx is still reachable, just yellow.
                ImGui.TextColored(r.State == ProbeState.Ok ? Green : Yellow, $"{r.StatusCode} ({r.ElapsedMs}ms)");
                break;
            case ProbeState.Failed:
                ImGui.TextColored(Red, $"{Loc.T("diag.state.failed")} ({r.ElapsedMs}ms)");
                if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(r.Detail))
                    ImGui.SetTooltip(r.Detail);
                break;
            default:
                ImGui.TextColored(Grey, "—");
                break;
        }
    }

    private void RunAll()
    {
        foreach (var t in _targets)
            RunBoth(t);
    }

    private void RunBoth(Target target)
    {
        RunProbe(target, Route.Direct);
        RunProbe(target, Route.Proxy);
    }

    private void RunProbe(Target target, Route route)
    {
        target.Set(route, new ProbeResult { State = ProbeState.Testing });
        var token = _cts.Token;
        var client = route == Route.Direct ? _directClient : _proxyClient;

        // Off the render thread so the game UI never stalls while a probe waits on the network.
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, target.Url);
                // Hands-off marker: our own interceptor must not rewrite, inject, wrap, or log this probe.
                req.Options.Set(HttpInterceptor.NoRewriteKey, true);
                using var resp = await client
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);
                sw.Stop();

                var code = (int)resp.StatusCode;
                target.Set(route, new ProbeResult
                {
                    // Any HTTP response proves the path works; a 4xx/5xx is still "reachable".
                    State = code < 400 ? ProbeState.Ok : ProbeState.Reachable,
                    StatusCode = code,
                    ElapsedMs = sw.ElapsedMilliseconds,
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                if (token.IsCancellationRequested)
                    return; // window closing; drop the result silently
                target.Set(route, new ProbeResult
                {
                    State = ProbeState.Failed,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Detail = Describe(ex),
                });
            }
        }, token);
    }

    private static string Describe(Exception ex)
    {
        // HttpClient surfaces its per-request timeout as a cancellation; report it as a timeout
        // because our own cancellation token is handled separately above.
        if (ex is TaskCanceledException or OperationCanceledException)
            return Loc.T("diag.err.timeout");

        var inner = ex;
        while (inner.InnerException != null)
            inner = inner.InnerException;
        return inner.Message;
    }

    private string DescribeRoute()
    {
        var s = _config.Settings;
        if (!s.Enabled)
            return Loc.T("diag.route.disabled");

        switch (s.Mode)
        {
            case NetworkMode.NoProxy:
                return Loc.T("diag.route.direct");

            case NetworkMode.ManualProxy:
                return Loc.T("diag.route.manual",
                    string.IsNullOrWhiteSpace(s.ManualProxyUrl) ? "—" : s.ManualProxyUrl);

            case NetworkMode.SystemProxy:
                string upstream;
                try
                {
                    upstream = _proxy.GetProxy(SampleDestination)?.ToString() ?? Loc.T("diag.route.direct");
                }
                catch (Exception ex)
                {
                    upstream = ex.Message;
                }
                return Loc.T("diag.route.system", upstream);

            default:
                return "—";
        }
    }

    private void CopyReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProxyPlugin network diagnostics");
        sb.AppendLine(Loc.T("diag.mode", DescribeRoute()));
        sb.AppendLine();

        foreach (var t in _targets)
        {
            sb.Append(Loc.T(t.NameKey)).Append("  [").Append(t.Url).AppendLine("]");
            sb.Append("  direct: ").AppendLine(FormatResult(t.Get(Route.Direct)));
            sb.Append("  proxy : ").AppendLine(FormatResult(t.Get(Route.Proxy)));
        }

        try { ImGui.SetClipboardText(sb.ToString()); } catch { /* clipboard is best-effort */ }
    }

    private static string FormatResult(ProbeResult r) => r.State switch
    {
        ProbeState.Ok => $"OK {r.StatusCode} {r.ElapsedMs}ms",
        ProbeState.Reachable => $"REACHABLE {r.StatusCode} {r.ElapsedMs}ms",
        ProbeState.Failed => $"FAIL {r.ElapsedMs}ms ({r.Detail})",
        ProbeState.Testing => "...",
        _ => "- (not tested)",
    };
}
