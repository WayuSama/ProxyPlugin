using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProxyPlugin.Core;

public sealed class MirrorRule
{
    // Hot-path safety net: a pathological pattern can never spin the request thread.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private Regex? _compiled;
    private bool _compileFailed;

    public string Id { get; set; } = "";

    /// <summary>
    /// Regex evaluated against the request's <c>host/path</c> (scheme, port, query and
    /// fragment are excluded and handled structurally by <see cref="RewriteEngine"/>).
    /// Anchor with <c>^</c> to match from the host, e.g. <c>^raw\.githubusercontent\.com</c>.
    /// Hosts are compared lowercase (<see cref="Uri.Host"/> is already normalized).
    /// </summary>
    public string Pattern { get; set; } = "";

    /// <summary>
    /// .NET regex replacement applied to the matched <c>host/path</c>; supports capture
    /// groups (<c>$1</c>). The text up to the first <c>/</c> becomes the new host.
    /// </summary>
    public string Replacement { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Lazily compiles <see cref="Pattern"/> once and caches it. Returns <c>null</c> when the
    /// pattern is empty or invalid so a bad rule is skipped instead of throwing on the hot path.
    /// </summary>
    internal Regex? GetRegex()
    {
        if (_compiled != null || _compileFailed)
            return _compiled;

        if (string.IsNullOrEmpty(Pattern))
        {
            _compileFailed = true;
            return null;
        }

        try
        {
            _compiled = new Regex(Pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            _compileFailed = true;
        }

        return _compiled;
    }
}
