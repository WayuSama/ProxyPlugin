namespace ProxyPlugin.Core;

public static class RewriteEngine
{
    public static bool TryRewrite(Uri input, IReadOnlyList<MirrorRule> rules, out Uri output)
    {
        output = input;
        if (rules is null || rules.Count == 0)
            return false;

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!rule.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(rule.MatchHost) || string.IsNullOrWhiteSpace(rule.TargetHost))
                continue;
            if (!string.Equals(input.Host, rule.MatchHost, StringComparison.OrdinalIgnoreCase))
                continue;

            string path = input.AbsolutePath;
            if (!string.IsNullOrEmpty(rule.MatchPathPrefix))
            {
                if (!path.StartsWith(rule.MatchPathPrefix, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrEmpty(rule.TargetPathPrefix))
                    path = rule.TargetPathPrefix + path.Substring(rule.MatchPathPrefix.Length);
            }

            var builder = new UriBuilder
            {
                Scheme = input.Scheme,
                Host = rule.TargetHost,
                Port = -1,
                Path = path,
                Query = input.Query.TrimStart('?'),
                Fragment = input.Fragment.TrimStart('#'),
            };
            output = builder.Uri;
            return true;
        }

        return false;
    }
}
