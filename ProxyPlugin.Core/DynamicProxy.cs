using System.Net;

namespace ProxyPlugin.Core;

public sealed class DynamicProxy : IWebProxy
{
    private readonly Func<ProxySettings> _settingsProvider;
    private readonly ISystemProxyReader _systemReader;

    private string? _cachedManualUrl;
    private Uri? _cachedManualUri;
    private readonly object _manualLock = new();

    public DynamicProxy(Func<ProxySettings> settingsProvider, ISystemProxyReader systemReader)
    {
        _settingsProvider = settingsProvider;
        _systemReader = systemReader;
    }

    public ICredentials? Credentials { get; set; }

    public Uri? GetProxy(Uri destination)
    {
        var settings = _settingsProvider();
        if (!settings.Enabled)
            return null;

        switch (settings.Mode)
        {
            case NetworkMode.NoProxy:
                return null;

            case NetworkMode.SystemProxy:
            {
                var inner = _systemReader.GetCurrentProxy();
                if (inner == null || inner.IsBypassed(destination))
                    return null;
                return inner.GetProxy(destination);
            }

            case NetworkMode.ManualProxy:
            {
                if (IsLoopback(destination))
                    return null;
                return GetManualUri(settings.ManualProxyUrl);
            }

            default:
                return null;
        }
    }

    public bool IsBypassed(Uri host)
    {
        var settings = _settingsProvider();
        if (!settings.Enabled)
            return true;

        switch (settings.Mode)
        {
            case NetworkMode.NoProxy:
                return true;

            case NetworkMode.SystemProxy:
            {
                var inner = _systemReader.GetCurrentProxy();
                return inner == null || inner.IsBypassed(host);
            }

            case NetworkMode.ManualProxy:
                return IsLoopback(host) || GetManualUri(settings.ManualProxyUrl) == null;

            default:
                return true;
        }
    }

    private static bool IsLoopback(Uri uri)
        => uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    private Uri? GetManualUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        lock (_manualLock)
        {
            if (string.Equals(url, _cachedManualUrl, StringComparison.Ordinal))
                return _cachedManualUri;

            _cachedManualUrl = url;
            _cachedManualUri = TryParseProxyUri(url);
            return _cachedManualUri;
        }
    }

    private static Uri? TryParseProxyUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        return uri.Scheme is "http" or "https" or "socks5" or "socks4" or "socks4a" ? uri : null;
    }
}
