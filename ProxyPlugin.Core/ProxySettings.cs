namespace ProxyPlugin.Core;

public sealed class ProxySettings
{
    public bool Enabled { get; set; } = true;
    public NetworkMode Mode { get; set; } = NetworkMode.NoProxy;
    public string? ManualProxyUrl { get; set; }
}
