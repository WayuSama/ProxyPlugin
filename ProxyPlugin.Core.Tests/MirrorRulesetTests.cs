using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class MirrorRulesetTests
{
    [Fact]
    public void FromJson_LoadsValidRules_AndRewrites()
    {
        const string json = """
        [
          { "Id": "gh-raw", "Pattern": "^raw\\.githubusercontent\\.com", "Replacement": "mirror.example.cn", "Enabled": true }
        ]
        """;
        var rs = MirrorRuleset.FromJson(json);
        Assert.Single(rs.Rules);
        Assert.True(rs.TryRewrite(new Uri("https://raw.githubusercontent.com/a"), out var o));
        Assert.Equal("mirror.example.cn", o.Host);
    }

    [Fact]
    public void FromJson_SkipsEntriesMissingPatternOrReplacement()
    {
        const string json = """
        [ { "Id": "bad" }, { "Pattern": "^h\\.com", "Replacement": "t.cn" } ]
        """;
        var rs = MirrorRuleset.FromJson(json);
        Assert.Single(rs.Rules);
    }

    [Fact]
    public void FromJson_DropsRulesWithInvalidRegex()
    {
        const string json = """
        [ { "Id": "bad", "Pattern": "[", "Replacement": "t.cn", "Enabled": true } ]
        """;
        var rs = MirrorRuleset.FromJson(json);
        Assert.Empty(rs.Rules);
    }

    [Fact]
    public void FromJson_MalformedJson_ReturnsEmpty()
    {
        var rs = MirrorRuleset.FromJson("not json {");
        Assert.Empty(rs.Rules);
        Assert.False(rs.TryRewrite(new Uri("https://x/y"), out _));
    }

    [Fact]
    public void FromJson_ToleratesCommentsTrailingCommasAndCaseInsensitiveKeys()
    {
        const string json = """
        [ /* a comment */
          { "pattern": "^h\\.com", "replacement": "t.cn", },
        ]
        """;
        var rs = MirrorRuleset.FromJson(json);
        Assert.Single(rs.Rules);
        Assert.Equal(@"^h\.com", rs.Rules[0].Pattern);
    }

    [Fact]
    public void FromJson_NullOrBlank_ReturnsEmpty()
    {
        Assert.Empty(MirrorRuleset.FromJson(null!).Rules);
        Assert.Empty(MirrorRuleset.FromJson("").Rules);
        Assert.Empty(MirrorRuleset.FromJson("   ").Rules);
    }
}
