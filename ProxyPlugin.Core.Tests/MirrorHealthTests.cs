using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

public class MirrorHealthTests
{
    [Fact]
    public void OpensAfterThreshold_ThenClosesAfterCooldown()
    {
        var clock = new FakeClock();
        var h = new MirrorHealth(clock, failureThreshold: 3, cooldown: TimeSpan.FromSeconds(30));

        Assert.False(h.IsOpen("m.cn"));
        h.ReportFailure("m.cn");
        h.ReportFailure("m.cn");
        Assert.False(h.IsOpen("m.cn"));
        h.ReportFailure("m.cn");
        Assert.True(h.IsOpen("m.cn"));

        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        Assert.False(h.IsOpen("m.cn"));
    }

    [Fact]
    public void Success_ResetsFailureCount()
    {
        var clock = new FakeClock();
        var h = new MirrorHealth(clock, failureThreshold: 2, cooldown: TimeSpan.FromSeconds(30));
        h.ReportFailure("m.cn");
        h.ReportSuccess("m.cn");
        h.ReportFailure("m.cn");
        Assert.False(h.IsOpen("m.cn"));
    }

    [Fact]
    public void Hosts_AreIndependent()
    {
        var clock = new FakeClock();
        var h = new MirrorHealth(clock, failureThreshold: 1, cooldown: TimeSpan.FromSeconds(30));
        h.ReportFailure("a.cn");
        Assert.True(h.IsOpen("a.cn"));
        Assert.False(h.IsOpen("b.cn"));
    }

    [Fact]
    public void Success_DuringOpenCooldown_ClosesImmediately()
    {
        var clock = new FakeClock();
        var h = new MirrorHealth(clock, failureThreshold: 2, cooldown: TimeSpan.FromSeconds(30));
        h.ReportFailure("m.cn");
        h.ReportFailure("m.cn");
        Assert.True(h.IsOpen("m.cn"));
        h.ReportSuccess("m.cn");
        Assert.False(h.IsOpen("m.cn"));
    }

    [Fact]
    public void HalfOpenProbeFailure_ReopensImmediately()
    {
        var clock = new FakeClock();
        var h = new MirrorHealth(clock, failureThreshold: 3, cooldown: TimeSpan.FromSeconds(30));
        h.ReportFailure("m.cn");
        h.ReportFailure("m.cn");
        h.ReportFailure("m.cn");
        Assert.True(h.IsOpen("m.cn"));

        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        Assert.False(h.IsOpen("m.cn")); // half-open probe allowed

        h.ReportFailure("m.cn"); // single probe failure (count not reset)
        Assert.True(h.IsOpen("m.cn"));
    }

    [Fact]
    public void HostKeys_AreCaseInsensitive()
    {
        var clock = new FakeClock();
        var h = new MirrorHealth(clock, failureThreshold: 1, cooldown: TimeSpan.FromSeconds(30));
        h.ReportFailure("M.CN");
        Assert.True(h.IsOpen("m.cn"));
    }
}
