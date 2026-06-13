using System.Net;
using System.Text;

namespace ProxyPlugin.Core;

public static class SystemProxyParser
{
    public static WebProxy? Parse(bool proxyEnable, string? proxyServer, string? proxyOverride)
    {
        if (!proxyEnable || string.IsNullOrWhiteSpace(proxyServer))
            return null;

        string? address = ResolveAddress(proxyServer);
        if (address == null)
            return null;

        if (!address.Contains("://", StringComparison.Ordinal))
            address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            return null;

        var (bypassList, bypassLocal) = ParseOverride(proxyOverride);
        return new WebProxy(uri)
        {
            BypassProxyOnLocal = bypassLocal,
            BypassList = bypassList.ToArray(),
        };
    }

    private static string? ResolveAddress(string proxyServer)
    {
        if (!proxyServer.Contains('=', StringComparison.Ordinal))
            return proxyServer.Trim();

        string? http = null, https = null;
        foreach (var part in proxyServer.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            string scheme = part[..eq].Trim();
            string value = part[(eq + 1)..].Trim();
            if (scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                https = value;
            else if (scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                http = value;
        }

        return https ?? http;
    }

    private static (List<string> list, bool local) ParseOverride(string? proxyOverride)
    {
        var list = new List<string>();
        bool local = false;
        if (string.IsNullOrWhiteSpace(proxyOverride))
            return (list, local);

        foreach (var raw in proxyOverride.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Equals("<local>", StringComparison.OrdinalIgnoreCase))
            {
                local = true;
                continue;
            }
            list.Add(WildcardToRegex(raw));
        }

        return (list, local);
    }

    private static string WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (char c in pattern)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '.' => "\\.",
                '?' => ".",
                _ => System.Text.RegularExpressions.Regex.Escape(c.ToString()),
            });
        }
        sb.Append('$');
        return sb.ToString();
    }
}
