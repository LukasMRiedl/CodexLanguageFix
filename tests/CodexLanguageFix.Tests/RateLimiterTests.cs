using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class RateLimiterTests
{
    [Fact]
    public void Reserve_RejectsTwentyFirstRequestWithinWindow()
    {
        var limiter = new SlidingWindowRateLimiter();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 20; index++)
        {
            limiter.Reserve(1, now.AddMilliseconds(index));
        }

        Assert.Throws<RateLimitException>(() => limiter.Reserve(1, now.AddSeconds(1)));
        limiter.Reserve(1, now.AddMinutes(1).AddSeconds(1));
    }

    [Fact]
    public void Reserve_RejectsCharacterLimit()
    {
        var limiter = new SlidingWindowRateLimiter();
        var now = DateTimeOffset.UtcNow;
        limiter.Reserve(70_000, now);

        var exception = Assert.Throws<RateLimitException>(() => limiter.Reserve(5_001, now));

        Assert.True(exception.RetryAfter > TimeSpan.Zero);
    }
}
