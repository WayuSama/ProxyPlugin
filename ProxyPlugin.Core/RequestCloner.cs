namespace ProxyPlugin.Core;

public static class RequestCloner
{
    public static HttpRequestMessage? TryClone(HttpRequestMessage source, Uri newUri)
    {
        if (source.Content != null)
            return null;

        var clone = new HttpRequestMessage(source.Method, newUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in (IDictionary<string, object?>)source.Options)
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;

        return clone;
    }
}
