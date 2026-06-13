using System;
using Dalamud.Plugin.Services;
using ProxyPlugin.Core;

namespace ProxyPlugin;

public sealed class DalamudInterceptorLog : IInterceptorLog
{
    private readonly IPluginLog _log;
    public DalamudInterceptorLog(IPluginLog log) => _log = log;
    public void Error(Exception ex, string message) => _log.Error(ex, message);
    public void Info(string message) => _log.Information(message);
}
