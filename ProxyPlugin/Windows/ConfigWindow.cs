using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ProxyPlugin.Core;

namespace ProxyPlugin.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly Uri SampleDestination = new("https://github.com/");

    private readonly Configuration _config;
    private readonly RequestLog _log;
    private readonly HttpInterceptor _interceptor;
    private readonly MirrorRuleset _ruleset;
    private readonly DynamicProxy _proxy;
    private string _manualUrlBuffer;
    private volatile string _testResult = "";

    public ConfigWindow(Plugin plugin, RequestLog log, HttpInterceptor interceptor, MirrorRuleset ruleset, DynamicProxy proxy)
        : base("ProxyPlugin Settings###ProxyPluginConfig")
    {
        _config = plugin.Configuration;
        _log = log;
        _interceptor = interceptor;
        _ruleset = ruleset;
        _proxy = proxy;
        _manualUrlBuffer = _config.Settings.ManualProxyUrl ?? "";
        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var s = _config.Settings;

        bool enabled = s.Enabled;
        if (ImGui.Checkbox("Enable ProxyPlugin", ref enabled))
        {
            s.Enabled = enabled;
            _config.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Network mode");

        if (DrawModeRadio("System proxy (follow OS, live)", NetworkMode.SystemProxy, s))
            OnModeChanged();
        if (DrawModeRadio("Manual proxy (http / socks5)", NetworkMode.ManualProxy, s))
            OnModeChanged();

        // Proxy configuration block (shown for both proxy modes): the manual URL field only applies
        // to manual mode, but the Test button stays visible in either proxy mode.
        if (s.Mode is NetworkMode.SystemProxy or NetworkMode.ManualProxy)
        {
            ImGui.Indent();
            if (s.Mode == NetworkMode.ManualProxy)
            {
                ImGui.SetNextItemWidth(360);
                if (ImGui.InputTextWithHint("##manualUrl", "http://127.0.0.1:7890 or socks5://127.0.0.1:1080", ref _manualUrlBuffer, 256))
                {
                    s.ManualProxyUrl = _manualUrlBuffer;
                    _config.Save();
                }
                ImGui.SameLine();
            }

            if (ImGui.Button("Test"))
                StartProxyTest();
            if (!string.IsNullOrEmpty(_testResult))
                ImGui.TextUnformatted(_testResult);
            ImGui.Unindent();
        }

        // No-proxy sits below the proxy configuration block.
        if (DrawModeRadio("No proxy (use built-in mirrors)", NetworkMode.NoProxy, s))
            OnModeChanged();

        ImGui.Separator();
        int activeRules = _ruleset.Rules.Count(r => r.Enabled);
        ImGui.TextUnformatted($"Mirror rules: {activeRules} active / {_ruleset.Rules.Count} total");
        if (!_interceptor.ForceInjectionAvailable)
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "Forced proxy unavailable on this runtime (baseline mode).");
        DrawMirrorRules();

        DrawLogTable();
    }

    private void OnModeChanged()
    {
        _testResult = "";
        _config.Save();
    }

    private string DescribeUpstream()
    {
        try
        {
            return _proxy.GetProxy(SampleDestination)?.ToString() ?? "direct";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    private static bool DrawModeRadio(string label, NetworkMode mode, ProxySettings s)
    {
        bool selected = s.Mode == mode;
        if (ImGui.RadioButton(label, selected) && !selected)
        {
            s.Mode = mode;
            return true;
        }
        return false;
    }

    private void DrawMirrorRules()
    {
        // Collapsed by default (no DefaultOpen flag).
        if (!ImGui.CollapsingHeader("Mirror rules##rulesHeader"))
            return;

        if (!ImGui.BeginTable("##rules", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 140)))
            return;

        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 32);
        ImGui.TableSetupColumn("Match", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var r in _ruleset.Rules)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(r.Enabled ? "Y" : "-");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(r.MatchHost + (r.MatchPathPrefix ?? ""));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(r.TargetHost + (r.TargetPathPrefix ?? ""));
        }

        ImGui.EndTable();
    }

    private void DrawLogTable()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Recent requests");
        if (!ImGui.BeginTable("##log", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("URL", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn("ms", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableHeadersRow();

        foreach (var e in _log.Snapshot())
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.FinalUrl);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(DescribeFlags(e));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.StatusCode > 0 ? e.StatusCode.ToString() : "ERR");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.ElapsedMs.ToString());
        }

        ImGui.EndTable();
    }

    private static string DescribeFlags(RequestLogEntry e) => (e.Rewritten, e.Proxied) switch
    {
        (true, true) => "Rewrite, Proxy",
        (true, false) => "Rewrite",
        (false, true) => "Proxy",
        _ => "direct",
    };

    private void StartProxyTest()
    {
        IWebProxy testProxy;
        string target;

        if (_config.Settings.Mode == NetworkMode.ManualProxy)
        {
            if (string.IsNullOrWhiteSpace(_manualUrlBuffer) || !Uri.TryCreate(_manualUrlBuffer, UriKind.Absolute, out var uri))
            {
                _testResult = "Invalid URL";
                return;
            }
            testProxy = new WebProxy(uri);
            target = uri.ToString();
        }
        else
        {
            // System mode: probe through whatever DynamicProxy resolves right now (system proxy or direct).
            testProxy = _proxy;
            target = DescribeUpstream();
        }

        _testResult = "Testing...";
        // Run off the render thread so the game UI never freezes during the probe.
        _ = Task.Run(() =>
        {
            try
            {
                using var handler = new SocketsHttpHandler { Proxy = testProxy, UseProxy = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                var req = new HttpRequestMessage(HttpMethod.Head, "https://www.gstatic.com/generate_204");
                // Mark as hands-off so our own interceptor does not redirect the probe.
                req.Options.Set(new HttpRequestOptionsKey<bool>("ProxyPlugin.NoRewrite"), true);
                using var resp = client.Send(req);
                _testResult = $"OK ({(int)resp.StatusCode}) via {target}";
            }
            catch (Exception ex)
            {
                _testResult = $"Failed: {ex.Message}";
            }
        });
    }
}
