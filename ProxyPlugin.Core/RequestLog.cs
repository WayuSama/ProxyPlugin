namespace ProxyPlugin.Core;

public sealed class RequestLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Host { get; set; } = "";
    public string OriginalUrl { get; set; } = "";
    public string FinalUrl { get; set; } = "";
    public bool Rewritten { get; set; }
    public bool Proxied { get; set; }
    public string Result { get; set; } = "";
    public long ElapsedMs { get; set; }
}

public sealed class RequestLog
{
    private readonly int _capacity;
    private readonly LinkedList<RequestLogEntry> _entries = new();
    private readonly object _lock = new();

    public RequestLog(int capacity = 200) => _capacity = Math.Max(1, capacity);

    public void Add(RequestLogEntry entry)
    {
        lock (_lock)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > _capacity)
                _entries.RemoveLast();
        }
    }

    public IReadOnlyList<RequestLogEntry> Snapshot()
    {
        lock (_lock)
            return _entries.ToList();
    }
}
