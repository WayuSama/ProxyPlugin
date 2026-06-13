using System.Collections.Concurrent;

namespace ProxyPlugin.Core;

public sealed class MirrorHealth
{
    private sealed class State
    {
        public int ConsecutiveFailures;
        public DateTime OpenUntilUtc = DateTime.MinValue;
    }

    private readonly IClock _clock;
    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldown;
    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);

    public MirrorHealth(IClock clock, int failureThreshold = 3, TimeSpan? cooldown = null)
    {
        _clock = clock;
        _failureThreshold = Math.Max(1, failureThreshold);
        _cooldown = cooldown ?? TimeSpan.FromSeconds(30);
    }

    public bool IsOpen(string host)
    {
        if (string.IsNullOrEmpty(host))
            return false;
        if (!_states.TryGetValue(host, out var s))
            return false;
        lock (s)
            return _clock.UtcNow < s.OpenUntilUtc;
    }

    public void ReportFailure(string host)
    {
        if (string.IsNullOrEmpty(host))
            return;
        var s = _states.GetOrAdd(host, _ => new State());
        lock (s)
        {
            s.ConsecutiveFailures++;
            if (s.ConsecutiveFailures >= _failureThreshold)
                s.OpenUntilUtc = _clock.UtcNow + _cooldown;
        }
    }

    public void ReportSuccess(string host)
    {
        if (string.IsNullOrEmpty(host))
            return;
        if (_states.TryGetValue(host, out var s))
        {
            lock (s)
            {
                s.ConsecutiveFailures = 0;
                s.OpenUntilUtc = DateTime.MinValue;
            }
        }
    }
}
