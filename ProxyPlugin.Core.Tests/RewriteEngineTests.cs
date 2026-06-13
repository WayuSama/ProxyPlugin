using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class RewriteEngineTests
{
    private static MirrorRule Raw() => new()
    {
        Id = "gh-raw", MatchHost = "raw.githubusercontent.com",
        TargetHost = "mirror.example.cn", Enabled = true,
    };

    [Fact]
    public void Rewrites_HostOnly_PreservingPathQuery()
    {
        var input = new Uri("https://raw.githubusercontent.com/org/repo/main/a.json?x=1");
        Assert.True(RewriteEngine.TryRewrite(input, new[] { Raw() }, out var output));
        Assert.Equal("https", output.Scheme);
        Assert.Equal("mirror.example.cn", output.Host);
        Assert.Equal("/org/repo/main/a.json", output.AbsolutePath);
        Assert.Equal("?x=1", output.Query);
    }

    [Fact]
    public void Preserves_WssScheme_ForWebSocket()
    {
        var input = new Uri("wss://raw.githubusercontent.com/socket");
        Assert.True(RewriteEngine.TryRewrite(input, new[] { Raw() }, out var output));
        Assert.Equal("wss", output.Scheme);
        Assert.Equal("mirror.example.cn", output.Host);
    }

    [Fact]
    public void NonMatchingHost_NotRewritten()
    {
        var input = new Uri("https://example.org/x");
        Assert.False(RewriteEngine.TryRewrite(input, new[] { Raw() }, out _));
    }

    [Fact]
    public void DisabledRule_Ignored()
    {
        var r = Raw(); r.Enabled = false;
        Assert.False(RewriteEngine.TryRewrite(new Uri("https://raw.githubusercontent.com/x"), new[] { r }, out _));
    }

    [Fact]
    public void PathPrefix_RemappedWhenTargetPrefixSet()
    {
        var rule = new MirrorRule
        {
            Id = "api", MatchHost = "api.crowdin.com", MatchPathPrefix = "/api/v2",
            TargetHost = "crowdin.example.cn", TargetPathPrefix = "/cr", Enabled = true,
        };
        var input = new Uri("https://api.crowdin.com/api/v2/projects?p=1");
        Assert.True(RewriteEngine.TryRewrite(input, new[] { rule }, out var output));
        Assert.Equal("crowdin.example.cn", output.Host);
        Assert.Equal("/cr/projects", output.AbsolutePath);
        Assert.Equal("?p=1", output.Query);
    }

    [Fact]
    public void PathPrefix_NotMatched_NoRewrite()
    {
        var rule = new MirrorRule { Id = "api", MatchHost = "api.crowdin.com", MatchPathPrefix = "/api/v2", TargetHost = "x.cn", Enabled = true };
        Assert.False(RewriteEngine.TryRewrite(new Uri("https://api.crowdin.com/other"), new[] { rule }, out _));
    }

    [Fact]
    public void FirstEnabledRule_Wins()
    {
        var r1 = new MirrorRule { Id = "1", MatchHost = "a.com", TargetHost = "first.cn", Enabled = true };
        var r2 = new MirrorRule { Id = "2", MatchHost = "a.com", TargetHost = "second.cn", Enabled = true };
        Assert.True(RewriteEngine.TryRewrite(new Uri("https://a.com/x"), new[] { r1, r2 }, out var o));
        Assert.Equal("first.cn", o.Host);
    }

    [Fact]
    public void ExplicitPort_IsDropped()
    {
        var input = new Uri("https://raw.githubusercontent.com:8443/x");
        Assert.True(RewriteEngine.TryRewrite(input, new[] { Raw() }, out var o));
        Assert.Equal(443, o.Port);
        Assert.DoesNotContain(":8443", o.ToString());
    }

    [Fact]
    public void Fragment_IsPreserved()
    {
        var input = new Uri("https://raw.githubusercontent.com/x#frag");
        Assert.True(RewriteEngine.TryRewrite(input, new[] { Raw() }, out var o));
        Assert.Equal("#frag", o.Fragment);
    }

    [Fact]
    public void HostMatch_IsCaseInsensitive()
    {
        var input = new Uri("https://RAW.GithubUserContent.COM/x");
        Assert.True(RewriteEngine.TryRewrite(input, new[] { Raw() }, out var o));
        Assert.Equal("mirror.example.cn", o.Host);
    }

    [Fact]
    public void MatchPathPrefix_WithoutTargetPrefix_KeepsPath()
    {
        var rule = new MirrorRule { Id = "p", MatchHost = "h.com", MatchPathPrefix = "/api", TargetHost = "t.cn", Enabled = true };
        Assert.True(RewriteEngine.TryRewrite(new Uri("https://h.com/api/x?q=1"), new[] { rule }, out var o));
        Assert.Equal("t.cn", o.Host);
        Assert.Equal("/api/x", o.AbsolutePath);
        Assert.Equal("?q=1", o.Query);
    }
}
