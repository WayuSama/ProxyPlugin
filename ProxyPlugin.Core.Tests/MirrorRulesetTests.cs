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
          { "Id": "gh-raw", "MatchHost": "raw.githubusercontent.com", "TargetHost": "mirror.example.cn", "Enabled": true }
        ]
        """;
        var rs = MirrorRuleset.FromJson(json);
        Assert.Single(rs.Rules);
        Assert.True(rs.TryRewrite(new Uri("https://raw.githubusercontent.com/a"), out var o));
        Assert.Equal("mirror.example.cn", o.Host);
    }

    [Fact]
    public void FromJson_SkipsInvalidEntries()
    {
        const string json = """
        [ { "Id": "bad" }, { "MatchHost": "h.com", "TargetHost": "t.cn" } ]
        """;
        var rs = MirrorRuleset.FromJson(json);
        Assert.Single(rs.Rules);
    }

    [Fact]
    public void FromJson_MalformedJson_ReturnsEmpty()
    {
        var rs = MirrorRuleset.FromJson("not json {");
        Assert.Empty(rs.Rules);
        Assert.False(rs.TryRewrite(new Uri("https://x/y"), out _));
    }
}
