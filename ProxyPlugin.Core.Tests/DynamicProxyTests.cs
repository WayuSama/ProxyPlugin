using System.Net;
using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

internal sealed class FakeReader : ISystemProxyReader
{
    public IWebProxy? Proxy;
    public IWebProxy? GetCurrentProxy() => Proxy;
}

public class DynamicProxyTests
{
    private static readonly Uri Dest = new("https://example.org/x");

    [Fact]
    public void NoProxyMode_ReturnsDirect()
    {
        var settings = new ProxySettings { Mode = NetworkMode.NoProxy };
        var p = new DynamicProxy(() => settings, new FakeReader());
        Assert.Null(p.GetProxy(Dest));
        Assert.True(p.IsBypassed(Dest));
    }

    [Fact]
    public void Disabled_ReturnsDirect_EvenInProxyMode()
    {
        var settings = new ProxySettings { Mode = NetworkMode.ManualProxy, ManualProxyUrl = "http://127.0.0.1:7890", Enabled = false };
        var p = new DynamicProxy(() => settings, new FakeReader());
        Assert.Null(p.GetProxy(Dest));
        Assert.True(p.IsBypassed(Dest));
    }

    [Fact]
    public void ManualMode_ReturnsParsedUpstream()
    {
        var settings = new ProxySettings { Mode = NetworkMode.ManualProxy, ManualProxyUrl = "http://127.0.0.1:7890" };
        var p = new DynamicProxy(() => settings, new FakeReader());
        Assert.Equal(new Uri("http://127.0.0.1:7890"), p.GetProxy(Dest));
        Assert.False(p.IsBypassed(Dest));
    }

    [Fact]
    public void ManualMode_Socks_Supported()
    {
        var settings = new ProxySettings { Mode = NetworkMode.ManualProxy, ManualProxyUrl = "socks5://127.0.0.1:1080" };
        var p = new DynamicProxy(() => settings, new FakeReader());
        Assert.Equal(new Uri("socks5://127.0.0.1:1080"), p.GetProxy(Dest));
    }

    [Fact]
    public void ManualMode_BypassesLoopbackDestination()
    {
        var settings = new ProxySettings { Mode = NetworkMode.ManualProxy, ManualProxyUrl = "http://127.0.0.1:7890" };
        var p = new DynamicProxy(() => settings, new FakeReader());
        Assert.True(p.IsBypassed(new Uri("http://localhost:5000/api")));
    }

    [Fact]
    public void ManualMode_InvalidUrl_IsBypassed()
    {
        var settings = new ProxySettings { Mode = NetworkMode.ManualProxy, ManualProxyUrl = "not a url" };
        var p = new DynamicProxy(() => settings, new FakeReader());
        Assert.True(p.IsBypassed(Dest));
        Assert.Null(p.GetProxy(Dest));
    }

    [Fact]
    public void SystemMode_DelegatesToReader_AndReflectsLiveChanges()
    {
        var settings = new ProxySettings { Mode = NetworkMode.SystemProxy };
        var reader = new FakeReader();
        var p = new DynamicProxy(() => settings, reader);
        Assert.Null(p.GetProxy(Dest));

        reader.Proxy = new WebProxy("http://10.0.0.1:8080");
        Assert.Equal(new Uri("http://10.0.0.1:8080"), p.GetProxy(Dest));
    }
}
