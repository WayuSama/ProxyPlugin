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
            _config.Save();
        if (DrawModeRadio("Manual proxy (http / socks5)", NetworkMode.ManualProxy, s))
            _config.Save();
        if (DrawModeRadio("No proxy (use built-in mirrors)", NetworkMode.NoProxy, s))
            _config.Save();

        if (s.Mode == NetworkMode.ManualProxy)
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(360);
            if (ImGui.InputTextWithHint("##manualUrl", "http://127.0.0.1:7890 or socks5://127.0.0.1:1080", ref _manualUrlBuffer, 256))
            {
                s.ManualProxyUrl = _manualUrlBuffer;
                _config.Save();
            }
            ImGui.SameLine();
            if (ImGui.Button("Test"))
                StartProxyTest(_manualUrlBuffer);
            if (!string.IsNullOrEmpty(_testResult))
                ImGui.TextUnformatted(_testResult);
            ImGui.Unindent();
        }

        if (s.Mode is NetworkMode.SystemProxy or NetworkMode.ManualProxy)
        {
            bool overlay = s.OverlayMirrorInProxyModes;
            if (ImGui.Checkbox("Also rewrite to mirrors in proxy mode (advanced)", ref overlay))
            {
                s.OverlayMirrorInProxyModes = overlay;
                _config.Save();
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted($"Force injection: {(_interceptor.ForceInjectionAvailable ? "available" : "DEGRADED (baseline only)")}");
        int activeRules = _ruleset.Rules.Count(r => r.Enabled);
        ImGui.TextUnformatted($"Mirror rules: {activeRules} active / {_ruleset.Rules.Count} total");
        ImGui.TextUnformatted($"Effective upstream (github.com): {DescribeUpstream()}");

        DrawLogTable();
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

    private void DrawLogTable()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Recent requests");
        if (!ImGui.BeginTable("##log", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Host", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableHeadersRow();

        foreach (var e in _log.Snapshot())
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.Host);
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{(e.Rewritten ? "R" : "-")}{(e.Proxied ? "P" : "-")}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.Result);
        }

        ImGui.EndTable();
    }

    private void StartProxyTest(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _testResult = "Invalid URL";
            return;
        }

        _testResult = "Testing...";
        // Run off the render thread so the game UI never freezes during the probe.
        _ = Task.Run(() =>
        {
            try
            {
                using var handler = new SocketsHttpHandler { Proxy = new WebProxy(uri), UseProxy = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                var req = new HttpRequestMessage(HttpMethod.Head, "https://www.gstatic.com/generate_204");
                // Mark as hands-off so our own interceptor does not redirect the probe.
                req.Options.Set(new HttpRequestOptionsKey<bool>("ProxyPlugin.NoRewrite"), true);
                using var resp = client.Send(req);
                _testResult = $"OK ({(int)resp.StatusCode})";
            }
            catch (Exception ex)
            {
                _testResult = $"Failed: {ex.Message}";
            }
        });
    }
}
