using System.Net;
using System.Net.Sockets;

namespace ProxyPlugin.Core;

public static class FailureClassifier
{
    public static bool IsFailureStatus(HttpStatusCode status)
        => (int)status >= 500 || status == HttpStatusCode.NotFound;

    public static bool IsTransportFailure(Exception ex) => ex switch
    {
        HttpRequestException => true,
        SocketException => true,
        TaskCanceledException => true,
        IOException => true,
        _ => false,
    };

    public static bool ShouldFallback(HttpResponseMessage? response, Exception? exception)
    {
        if (exception != null)
            return IsTransportFailure(exception);
        return response != null && IsFailureStatus(response.StatusCode);
    }
}
