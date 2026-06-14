using System.Text.Json;

namespace ProxyPlugin.Core;

public sealed class MirrorRuleset
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Built-in mirror ruleset, compiled into the plugin (no external file). These are
    // TEMPLATES: replace each Replacement host with a real maintainer-operated
    // mirror/reverse-proxy domain, then set "Enabled": true before release. Each rule
    // rewrites a request via a regex over "host/path"; scheme, port, query and fragment
    // are preserved automatically, and the text up to the first '/' in Replacement
    // becomes the new (mirror) host. Rules ship DISABLED so a default install never
    // rewrites to a dead mirror.
    private const string DefaultRulesJson = """
    [
      {
        "Id": "github-raw",
        "Pattern": "^raw\\.githubusercontent\\.com",
        "Replacement": "raw.mirror.example.cn",
        "Enabled": false
      },
      {
        "Id": "github-release-assets",
        "Pattern": "^objects\\.githubusercontent\\.com",
        "Replacement": "objects.mirror.example.cn",
        "Enabled": false
      },
      {
        "Id": "crowdin-api",
        "Pattern": "^api\\.crowdin\\.com/api/v2",
        "Replacement": "crowdin.mirror.example.cn/api/v2",
        "Enabled": false
      }
    ]
    """;

    public IReadOnlyList<MirrorRule> Rules { get; }

    /// <summary>
    /// Keeps only rules with a non-empty replacement and a compilable pattern; the rest are
    /// dropped here so the hot path never has to revalidate. Pre-compiles each survivor.
    /// </summary>
    public MirrorRuleset(IReadOnlyList<MirrorRule>? rules)
    {
        var valid = new List<MirrorRule>(rules?.Count ?? 0);
        if (rules != null)
        {
            foreach (var r in rules)
            {
                if (r is null || string.IsNullOrEmpty(r.Replacement) || r.GetRegex() is null)
                    continue;
                valid.Add(r);
            }
        }

        Rules = valid;
    }

    public static MirrorRuleset FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MirrorRuleset(new List<MirrorRule>());

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MirrorRule>>(json, Options);
            return new MirrorRuleset(parsed);
        }
        catch (JsonException)
        {
            return new MirrorRuleset(new List<MirrorRule>());
        }
    }

    /// <summary>
    /// The built-in ruleset compiled into the plugin (see <c>DefaultRulesJson</c>).
    /// </summary>
    public static MirrorRuleset Default() => FromJson(DefaultRulesJson);

    public bool TryRewrite(Uri input, out Uri output) => RewriteEngine.TryRewrite(input, Rules, out output);
}
