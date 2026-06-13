using System;
using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class ClockTests
{
    [Fact]
    public void SystemClock_ReturnsUtcNowMonotonicWithinTolerance()
    {
        IClock clock = new SystemClock();
        var before = DateTime.UtcNow;
        var v = clock.UtcNow;
        var after = DateTime.UtcNow;
        Assert.True(v >= before.AddSeconds(-1) && v <= after.AddSeconds(1));
    }
}
