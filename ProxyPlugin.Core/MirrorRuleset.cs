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

    public bool TryRewrite(Uri input, out Uri output) => RewriteEngine.TryRewrite(input, Rules, out output);
}
