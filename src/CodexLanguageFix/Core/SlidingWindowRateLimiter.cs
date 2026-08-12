namespace CodexLanguageFix.Core;

public sealed class SlidingWindowRateLimiter(
    int maximumRequests = 20,
    int maximumCharacters = 75_000,
    TimeSpan? window = null,
    AppLocalizer? localizer = null)
{
    private readonly int _maximumRequests = maximumRequests;
    private readonly int _maximumCharacters = maximumCharacters;
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(1);
    private readonly Queue<Entry> _entries = new();
    private readonly object _gate = new();
    private readonly AppLocalizer _localizer = localizer ?? new AppLocalizer();

    public void Reserve(int characterCount, DateTimeOffset? now = null)
    {
        if (characterCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterCount));
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        lock (_gate)
        {
            Purge(timestamp);
            var characterTotal = _entries.Sum(entry => entry.CharacterCount);
            if (_entries.Count >= _maximumRequests || characterTotal + characterCount > _maximumCharacters)
            {
                var retryAt = _entries.Count > 0 ? _entries.Peek().Timestamp + _window : timestamp + _window;
                var retryAfter = retryAt > timestamp ? retryAt - timestamp : TimeSpan.FromSeconds(1);
                throw new RateLimitException(
                    _localizer.Get(AppText.PublicRateLimit, Math.Ceiling(retryAfter.TotalSeconds)),
                    retryAfter);
            }

            _entries.Enqueue(new Entry(timestamp, characterCount));
        }
    }

    private void Purge(DateTimeOffset now)
    {
        while (_entries.TryPeek(out var entry) && now - entry.Timestamp >= _window)
        {
            _entries.Dequeue();
        }
    }

    private sealed record Entry(DateTimeOffset Timestamp, int CharacterCount);
}
