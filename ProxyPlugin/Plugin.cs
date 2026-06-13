using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ProxyPlugin.Core;
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
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _requestLog = new RequestLog();
        _health = new MirrorHealth(new SystemClock());
        _ruleset = MirrorRuleset.FromJson(LoadEmbeddedRules());
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

        _configWindow = new ConfigWindow(this, _requestLog, _interceptor);
        _windowSystem.AddWindow(_configWindow);

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open ProxyPlugin settings.",
        });

        Log.Information($"ProxyPlugin loaded. Rules: {_ruleset.Rules.Count}, ForceInjection: {_interceptor.ForceInjectionAvailable}");
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        _windowSystem.RemoveAllWindows();

        _watcher.Dispose();
        _interceptor.Dispose();

        HttpClient.DefaultProxy = _previousDefaultProxy ?? new WebProxy();
        WebRequest.DefaultWebProxy = _previousWebRequestProxy;

        Configuration.Save();
    }

    private static string LoadEmbeddedRules()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("mirror-rules.json", StringComparison.Ordinal));
        if (name == null)
            return "[]";
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null)
            return "[]";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void OnCommand(string command, string args) => ToggleConfigUi();

    private void ToggleConfigUi() => _configWindow.Toggle();
}
