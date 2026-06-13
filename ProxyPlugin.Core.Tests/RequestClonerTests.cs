using System.Net.Http.Headers;
using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class RequestClonerTests
{
    [Fact]
    public void Clone_BodylessGet_CopiesMethodVersionHeadersAndSetsNewUri()
    {
        var src = new HttpRequestMessage(HttpMethod.Get, "https://mirror.cn/a") { Version = new Version(2, 0) };
        src.Headers.TryAddWithoutValidation("User-Agent", "Test/1.0");
        var origin = new Uri("https://origin.com/a");

        var clone = RequestCloner.TryClone(src, origin);

        Assert.NotNull(clone);
        Assert.Equal(HttpMethod.Get, clone!.Method);
        Assert.Equal(origin, clone.RequestUri);
        Assert.Equal(new Version(2, 0), clone.Version);
        Assert.True(clone.Headers.TryGetValues("User-Agent", out var ua));
        Assert.Equal("Test/1.0", string.Join("", ua!));
    }

    [Fact]
    public void Clone_RequestWithBody_ReturnsNull()
    {
        var src = new HttpRequestMessage(HttpMethod.Post, "https://mirror.cn/a")
        {
            Content = new StringContent("payload"),
        };
        Assert.Null(RequestCloner.TryClone(src, new Uri("https://origin.com/a")));
    }
}
