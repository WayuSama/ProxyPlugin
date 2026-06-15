using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ProxyPlugin.Core;
using ProxyPlugin.Localization;

namespace ProxyPlugin.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly Uri SampleDestination = new("https://github.com/");

    private static readonly UiLanguage[] LanguageOptions =
        { UiLanguage.Auto, UiLanguage.English, UiLanguage.ChineseSimplified };

    private readonly Configuration _config;
    private readonly RequestLog _log;
    private readonly HttpInterceptor _interceptor;
    private readonly MirrorRuleset _ruleset;
    private readonly DynamicProxy _proxy;
    private readonly DiagnosticsWindow _diagnostics;
    private string _manualUrlBuffer;
    private volatile string _testResult = "";

    public ConfigWindow(Plugin plugin, RequestLog log, HttpInterceptor interceptor, MirrorRuleset ruleset, DynamicProxy proxy, DiagnosticsWindow diagnostics)
        : base("ProxyPlugin Settings###ProxyPluginConfig")
    {
        _config = plugin.Configuration;
        _log = log;
        _interceptor = interceptor;
        _ruleset = ruleset;
        _proxy = proxy;
        _diagnostics = diagnostics;
        _manualUrlBuffer = _config.Settings.ManualProxyUrl ?? "";
        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Keep the title localized while preserving a stable ImGui id (the part after ###).
        WindowName = Loc.T("window.title") + "###ProxyPluginConfig";

        var s = _config.Settings;

        DrawLanguageSelector();
        ImGui.Separator();

        bool enabled = s.Enabled;
        if (ImGui.Checkbox(Loc.T("enable"), ref enabled))
        {
            s.Enabled = enabled;
            _config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("diag.open")))
            _diagnostics.Toggle();

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("mode.header"));

        if (DrawModeRadio(Loc.T("mode.system"), NetworkMode.SystemProxy, s))
            OnModeChanged();
        if (DrawModeRadio(Loc.T("mode.manual"), NetworkMode.ManualProxy, s))
            OnModeChanged();

        // Proxy configuration block (shown for both proxy modes): the manual URL field only applies
        // to manual mode, but the Test button stays visible in either proxy mode.
        if (s.Mode is NetworkMode.SystemProxy or NetworkMode.ManualProxy)
        {
            ImGui.Indent();
            if (s.Mode == NetworkMode.ManualProxy)
            {
                ImGui.SetNextItemWidth(360);
                if (ImGui.InputTextWithHint("##manualUrl", Loc.T("manual.hint"), ref _manualUrlBuffer, 256))
                {
                    s.ManualProxyUrl = _manualUrlBuffer;
                    _config.Save();
                }
                ImGui.SameLine();
            }

            if (ImGui.Button(Loc.T("test")))
                StartProxyTest();
            if (!string.IsNullOrEmpty(_testResult))
                ImGui.TextUnformatted(_testResult);
            ImGui.Unindent();
        }

        if (DrawModeRadio(Loc.T("mode.none"), NetworkMode.NoProxy, s))
            OnModeChanged();

        ImGui.Separator();
        int activeRules = _ruleset.Rules.Count(r => r.Enabled);
        ImGui.TextUnformatted(Loc.T("mirror.count", activeRules, _ruleset.Rules.Count));
        if (!_interceptor.ForceInjectionAvailable)
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), Loc.T("force.degraded"));
        DrawMirrorRules();

        DrawLogTable();
    }

    private void DrawLanguageSelector()
    {
        ImGui.TextUnformatted(Loc.T("lang.label"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        if (!ImGui.BeginCombo("##lang", LanguageLabel(_config.Language)))
            return;

        foreach (var lang in LanguageOptions)
        {
            if (ImGui.Selectable(LanguageLabel(lang), lang == _config.Language) && lang != _config.Language)
            {
                _config.Language = lang;
                Loc.Apply(lang, Plugin.PluginInterface.UiLanguage);
                _config.Save();
            }
        }

        ImGui.EndCombo();
    }

    private static string LanguageLabel(UiLanguage lang) => lang switch
    {
        UiLanguage.English => Loc.T("lang.english"),
        UiLanguage.ChineseSimplified => Loc.T("lang.chinese"),
        _ => Loc.T("lang.auto"),
    };

    private void OnModeChanged()
    {
        _testResult = "";
        _config.Save();
    }

    private string DescribeUpstream()
    {
        try
        {
            return _proxy.GetProxy(SampleDestination)?.ToString() ?? Loc.T("upstream.direct");
        }
        catch (Exception ex)
        {
            return Loc.T("upstream.error", ex.Message);
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
        if (!ImGui.CollapsingHeader(Loc.T("mirror.header") + "##rulesHeader"))
            return;

        if (!ImGui.BeginTable("##rules", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 140)))
            return;

        ImGui.TableSetupColumn(Loc.T("mirror.col.on"), ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn(Loc.T("mirror.col.match"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("mirror.col.target"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var rules = _ruleset.Rules;
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            bool enabled = r.Enabled;
            // Index-based id keeps each checkbox unique even if two rules share an Id.
            if (ImGui.Checkbox($"##rule{i}", ref enabled))
                ToggleRule(r, enabled);

            ImGui.TableNextColumn(); ImGui.TextUnformatted(r.Pattern);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(r.Replacement);
        }

        ImGui.EndTable();
    }

    // Flip the live rule (the rewrite engine reads Enabled per request) and persist the
    // choice so it survives a plugin reload, which otherwise resets to the built-in defaults.
    private void ToggleRule(MirrorRule rule, bool enabled)
    {
        rule.Enabled = enabled;
        if (!string.IsNullOrEmpty(rule.Id))
            _config.RuleOverrides[rule.Id] = enabled;
        _config.Save();
    }

    private void DrawLogTable()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(Loc.T("log.header"));
        if (!ImGui.BeginTable("##log", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn(Loc.T("log.col.time"), ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn(Loc.T("log.col.url"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("log.col.flags"), ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn(Loc.T("log.col.ver"), ImGuiTableColumnFlags.WidthFixed, 42);
        ImGui.TableSetupColumn(Loc.T("log.col.code"), ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn(Loc.T("log.col.ms"), ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableHeadersRow();

        foreach (var e in _log.Snapshot())
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.FinalUrl);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(DescribeFlags(e));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.HttpVersion);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.StatusCode > 0 ? e.StatusCode.ToString() : Loc.T("log.err"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.ElapsedMs.ToString());
        }

        ImGui.EndTable();
    }

    private static string DescribeFlags(RequestLogEntry e) => (e.Rewritten, e.Proxied) switch
    {
        (true, true) => Loc.T("flag.rewriteProxy"),
        (true, false) => Loc.T("flag.rewrite"),
        (false, true) => Loc.T("flag.proxy"),
        _ => Loc.T("flag.direct"),
    };

    private void StartProxyTest()
    {
        IWebProxy testProxy;
        string target;

        if (_config.Settings.Mode == NetworkMode.ManualProxy)
        {
            if (string.IsNullOrWhiteSpace(_manualUrlBuffer) || !Uri.TryCreate(_manualUrlBuffer, UriKind.Absolute, out var uri))
            {
                _testResult = Loc.T("test.invalid");
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

        _testResult = Loc.T("test.testing");
        // Run off the render thread so the game UI never freezes during the probe.
        _ = Task.Run(() =>
        {
            try
            {
                using var handler = new SocketsHttpHandler { Proxy = testProxy, UseProxy = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                var req = new HttpRequestMessage(HttpMethod.Head, "https://www.gstatic.com/generate_204");
                // Mark as hands-off so our own interceptor does not redirect the probe.
                req.Options.Set(HttpInterceptor.NoRewriteKey, true);
                using var resp = client.Send(req);
                _testResult = Loc.T("test.ok", (int)resp.StatusCode, target);
            }
            catch (Exception ex)
            {
                _testResult = Loc.T("test.failed", ex.Message);
            }
        });
    }
}
