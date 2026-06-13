using ProxyPlugin.Core;
using Xunit;

namespace ProxyPlugin.Core.Tests;

public class ProxySettingsTests
{
    [Fact]
    public void Defaults_AreNoProxyEnabledNoOverlay()
    {
        var s = new ProxySettings();
        Assert.True(s.Enabled);
        Assert.Equal(NetworkMode.NoProxy, s.Mode);
        Assert.Null(s.ManualProxyUrl);
        Assert.False(s.OverlayMirrorInProxyModes);
    }
}
