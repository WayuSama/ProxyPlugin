using System.Net;
using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class FailureClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void IsFailureStatus_Matches(HttpStatusCode code, bool expected)
        => Assert.Equal(expected, FailureClassifier.IsFailureStatus(code));

    [Fact]
    public void TransportExceptions_AreFailures()
    {
        Assert.True(FailureClassifier.IsTransportFailure(new HttpRequestException("boom")));
        Assert.True(FailureClassifier.IsTransportFailure(new TaskCanceledException("timeout")));
        Assert.True(FailureClassifier.IsTransportFailure(new System.Net.Sockets.SocketException()));
    }

    [Fact]
    public void ShouldFallback_TrueOnExceptionOrBadStatus_FalseOnSuccess()
    {
        using var ok = new HttpResponseMessage(HttpStatusCode.OK);
        using var bad = new HttpResponseMessage(HttpStatusCode.BadGateway);
        Assert.False(FailureClassifier.ShouldFallback(ok, null));
        Assert.True(FailureClassifier.ShouldFallback(bad, null));
        Assert.True(FailureClassifier.ShouldFallback(null, new HttpRequestException()));
        Assert.False(FailureClassifier.ShouldFallback(null, new OperationCanceledException()));
    }
}
