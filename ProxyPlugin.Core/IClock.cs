namespace ProxyPlugin.Core;

public interface IClock
{
    DateTime UtcNow { get; }
}
