using System.Text.RegularExpressions;

namespace ProxyPlugin.Core;

public static class RewriteEngine
{
    public static bool TryRewrite(Uri input, IReadOnlyList<MirrorRule> rules, out Uri output)
    {
        output = input;
        if (rules is null || rules.Count == 0)
            return false;

        // Match against "host/path" only. Scheme, port, query and fragment are reattached
        // structurally below, so a rule author can never accidentally break them and the
        // regex stays focused on the authority + path it actually cares about.
        string source = input.Host + input.AbsolutePath;

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!rule.Enabled)
                continue;

            var regex = rule.GetRegex();
            if (regex is null)
                continue;

            string rewritten;
            try
            {
                // Replace returns the original instance when nothing matches, so the
                // equality check below doubles as the "did this rule apply?" test.
                rewritten = regex.Replace(source, rule.Replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (string.IsNullOrEmpty(rewritten) || rewritten == source)
                continue;

            int slash = rewritten.IndexOf('/');
            string newHost = slash < 0 ? rewritten : rewritten.Substring(0, slash);
            string newPath = slash < 0 ? "/" : rewritten.Substring(slash);
            if (newHost.Length == 0)
                continue;

            try
            {
                var builder = new UriBuilder
                {
                    Scheme = input.Scheme,
                    Host = newHost,
                    Port = -1,
                    Path = newPath,
                    Query = input.Query.TrimStart('?'),
                    Fragment = input.Fragment.TrimStart('#'),
                };
                output = builder.Uri;
                return true;
            }
            catch (UriFormatException)
            {
                // The replacement produced an invalid authority; skip this rule rather than
                // letting a malformed mirror config surface on the caller's request.
                continue;
            }
        }

        return false;
    }
}
