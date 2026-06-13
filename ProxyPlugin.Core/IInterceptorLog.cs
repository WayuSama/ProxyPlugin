namespace ProxyPlugin.Core;

public interface IInterceptorLog
{
    void Error(Exception ex, string message);
    void Info(string message);
}
