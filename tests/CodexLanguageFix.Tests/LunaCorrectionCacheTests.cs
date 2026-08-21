using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class LunaCorrectionCacheTests
{
    [Fact]
    public void Cache_UsesProfileAndTextAndEvictsLeastRecentlyUsedEntry()
    {
        var cache = new LunaCorrectionCache(2);
        var first = LunaCorrectionCache.CreateKey("eins", "profile-a");
        var second = LunaCorrectionCache.CreateKey("zwei", "profile-a");
        var third = LunaCorrectionCache.CreateKey("drei", "profile-a");

        cache.Set(first, new LunaCachedCorrection("Eins", 1));
        cache.Set(second, new LunaCachedCorrection("Zwei", 1));
        Assert.True(cache.TryGet(first, out _));
        cache.Set(third, new LunaCachedCorrection("Drei", 1));

        Assert.False(cache.TryGet(second, out _));
        Assert.True(cache.TryGet(first, out var correction));
        Assert.Equal("Eins", correction.CorrectedText);
        Assert.NotEqual(first, LunaCorrectionCache.CreateKey("eins", "profile-b"));
    }
}
