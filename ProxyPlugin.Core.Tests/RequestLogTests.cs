using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class RequestLogTests
{
    [Fact]
    public void Add_KeepsNewestFirst_AndCapsCapacity()
    {
        var log = new RequestLog(capacity: 2);
        log.Add(new RequestLogEntry { Host = "a" });
        log.Add(new RequestLogEntry { Host = "b" });
        log.Add(new RequestLogEntry { Host = "c" });

        var snap = log.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal("c", snap[0].Host);
        Assert.Equal("b", snap[1].Host);
    }
}
