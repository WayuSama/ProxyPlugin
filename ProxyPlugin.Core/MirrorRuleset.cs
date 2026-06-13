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

    public MirrorRuleset(IReadOnlyList<MirrorRule> rules) => Rules = rules;

    public static MirrorRuleset FromJson(string json)
    {
        List<MirrorRule> valid = new();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<MirrorRule>>(json, Options);
            if (parsed != null)
            {
                foreach (var r in parsed)
                {
                    if (r != null && !string.IsNullOrWhiteSpace(r.MatchHost) && !string.IsNullOrWhiteSpace(r.TargetHost))
                        valid.Add(r);
                }
            }
        }
        catch (JsonException)
        {
        }

        return new MirrorRuleset(valid);
    }

    public bool TryRewrite(Uri input, out Uri output) => RewriteEngine.TryRewrite(input, Rules, out output);
}
