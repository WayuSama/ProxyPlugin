using System.Net;
using Microsoft.Win32;

namespace ProxyPlugin.Core;

public sealed class WinHttpSystemProxyReader : ISystemProxyReader
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(5);
    private readonly object _lock = new();
    private IWebProxy? _cached;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public IWebProxy? GetCurrentProxy()
    {
        lock (_lock)
        {
            if (DateTime.UtcNow - _cachedAtUtc < _ttl)
                return _cached;
            _cached = ReadFromRegistry();
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    public void Invalidate()
    {
        lock (_lock)
            _cachedAtUtc = DateTime.MinValue;
    }

    private static IWebProxy? ReadFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key == null)
                return null;
            bool enable = (key.GetValue("ProxyEnable") as int?) == 1;
            var server = key.GetValue("ProxyServer") as string;
            var over = key.GetValue("ProxyOverride") as string;
            return SystemProxyParser.Parse(enable, server, over);
        }
        catch
        {
            return null;
        }
    }
}
