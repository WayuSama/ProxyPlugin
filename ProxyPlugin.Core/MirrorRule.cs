namespace ProxyPlugin.Core;

public sealed class MirrorRule
{
    public string Id { get; set; } = "";
    public string MatchHost { get; set; } = "";
    public string? MatchPathPrefix { get; set; }
    public string TargetHost { get; set; } = "";
    public string? TargetPathPrefix { get; set; }
    public bool Enabled { get; set; } = true;
}
