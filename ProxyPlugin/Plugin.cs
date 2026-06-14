using System;
using System.Net;
using System.Net.Http;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ProxyPlugin.Core;
using ProxyPlugin.Localization;
using ProxyPlugin.Windows;

namespace ProxyPlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/proxyplugin";

    public Configuration Configuration { get; }

    private readonly WindowSystem _windowSystem = new("ProxyPlugin");
    private readonly ConfigWindow _configWindow;

    private readonly WinHttpSystemProxyReader _systemReader;
    private readonly DynamicProxy _proxy;
    private readonly MirrorRuleset _ruleset;
    private readonly MirrorHealth _health;
    private readonly RequestLog _requestLog;
    private readonly HttpInterceptor _interceptor;
    private readonly SystemProxyWatcher _watcher;
    private readonly IWebProxy? _previousDefaultProxy;
    private readonly IWebProxy? _previousWebRequestProxy;

    public Plugin()
    {
        // Must run before any Harmony type is touched (i.e. before HttpInterceptor below): hosts
        // 0Harmony in the non-collectible Default ALC so MonoMod can operate. See HarmonyLoader.
        HarmonyLoader.EnsureLoaded(PluginInterface.AssemblyLocation.DirectoryName!);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Loc.Apply(Configuration.Language, PluginInterface.UiLanguage);

        _requestLog = new RequestLog();
        _health = new MirrorHealth(new SystemClock());
        _ruleset = MirrorRuleset.Default();
        _ruleset.ApplyEnabledOverrides(Configuration.RuleOverrides);
        _systemReader = new WinHttpSystemProxyReader();
        _proxy = new DynamicProxy(() => Configuration.Settings, _systemReader);

        _previousDefaultProxy = HttpClient.DefaultProxy;
        _previousWebRequestProxy = WebRequest.DefaultWebProxy;
        HttpClient.DefaultProxy = _proxy;
        WebRequest.DefaultWebProxy = _proxy;

        var logger = new DalamudInterceptorLog(Log);
        _interceptor = new HttpInterceptor(_proxy, _ruleset, _health, _requestLog, () => Configuration.Settings, logger);
        _interceptor.Install();

        _watcher = new SystemProxyWatcher(_systemReader.Invalidate, logger);
        _watcher.Start();

        _configWindow = new ConfigWindow(this, _requestLog, _interceptor, _ruleset, _proxy);
        _windowSystem.AddWindow(_configWindow);

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;
        PluginInterface.LanguageChanged += OnUiLanguageChanged;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.T("cmd.help"),
        });

        Log.Information($"ProxyPlugin loaded. Rules: {_ruleset.Rules.Count}, ForceInjection: {_interceptor.ForceInjectionAvailable}");
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        PluginInterface.LanguageChanged -= OnUiLanguageChanged;
        _windowSystem.RemoveAllWindows();

        _watcher.Dispose();
        _interceptor.Dispose();

        // Force any handler that already had DynamicProxy reflected into its built chain to
        // resolve direct, so disabling the plugin stops all interception even for live handlers.
        _proxy.Shutdown();

        HttpClient.DefaultProxy = _previousDefaultProxy ?? new WebProxy();
        WebRequest.DefaultWebProxy = _previousWebRequestProxy;

        Configuration.Save();
    }

    private void OnCommand(string command, string args) => ToggleConfigUi();

    private void ToggleConfigUi() => _configWindow.Toggle();

    // Re-resolve strings when Dalamud's UI language changes (only matters while in Auto mode).
    private void OnUiLanguageChanged(string langCode) => Loc.Apply(Configuration.Language, langCode);
}
