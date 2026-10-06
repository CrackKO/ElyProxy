namespace ElyProxy.Services;

public sealed class AutoReconnectPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private DateTime _lastAttemptUtc;
    private int _attemptCount;

    public AutoReconnectPolicy(int maxAttempts, TimeSpan window)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        _maxAttempts = maxAttempts;
        _window = window;
    }

    public bool TryBeginAttempt(DateTime nowUtc)
    {
        TimeSpan elapsed = nowUtc - _lastAttemptUtc;
        if (_lastAttemptUtc == default || elapsed < TimeSpan.Zero || elapsed > _window)
        {
            _attemptCount = 0;
        }

        _lastAttemptUtc = nowUtc;
        _attemptCount++;
        return _attemptCount <= _maxAttempts;
    }

    public void Reset()
    {
        _attemptCount = 0;
        _lastAttemptUtc = default;
    }
}
