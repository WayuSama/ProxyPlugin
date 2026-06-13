using System.Net;

namespace ProxyPlugin.Core;

public interface ISystemProxyReader
{
    IWebProxy? GetCurrentProxy();
}
