using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class SystemProxyParserTests
{
    [Fact]
    public void Disabled_ReturnsNull()
        => Assert.Null(SystemProxyParser.Parse(false, "127.0.0.1:7890", null));

    [Fact]
    public void EmptyServer_ReturnsNull()
        => Assert.Null(SystemProxyParser.Parse(true, "", null));

    [Fact]
    public void SingleHostPort_ParsedWithHttpScheme()
    {
        var proxy = SystemProxyParser.Parse(true, "127.0.0.1:7890", null);
        Assert.NotNull(proxy);
        Assert.Equal(new Uri("http://127.0.0.1:7890"), proxy!.GetProxy(new Uri("https://example.org")));
    }

    [Fact]
    public void PerProtocol_PrefersHttps()
    {
        var proxy = SystemProxyParser.Parse(true, "http=10.0.0.1:80;https=10.0.0.2:443", null);
        Assert.NotNull(proxy);
        Assert.Equal(new Uri("http://10.0.0.2:443"), proxy!.GetProxy(new Uri("https://example.org")));
    }

    [Fact]
    public void Override_LocalAndWildcard_AreBypassed()
    {
        var proxy = SystemProxyParser.Parse(true, "127.0.0.1:7890", "<local>;*.internal.corp");
        Assert.NotNull(proxy);
        Assert.True(proxy!.IsBypassed(new Uri("http://api.internal.corp/x")));
    }
}
