using WhileYoureUp;
using Xunit;

namespace WhileYoureUp.Tests;

public class GenerationalCacheTests
{
    [Fact]
    public void Stored_value_is_returned_until_the_cache_is_invalidated()
    {
        var cache = new GenerationalCache<string, int>(16);
        cache.Set("a", 1);

        Assert.True(cache.TryGetValue("a", out var value));
        Assert.Equal(1, value);

        cache.Invalidate();

        Assert.False(cache.TryGetValue("a", out _));
        Assert.Equal(0, cache.LiveCount);
    }

    [Fact]
    public void Value_set_after_an_invalidation_replaces_the_stale_one()
    {
        var cache = new GenerationalCache<string, int>(16);
        cache.Set("a", 1);
        cache.Invalidate();
        cache.Set("a", 2);

        Assert.True(cache.TryGetValue("a", out var value));
        Assert.Equal(2, value);
        Assert.Equal(1, cache.LiveCount);
    }

    [Fact]
    public void Setting_the_same_key_twice_in_one_generation_counts_it_once()
    {
        var cache = new GenerationalCache<string, int>(16);
        cache.Set("a", 1);
        cache.Set("a", 2);

        Assert.Equal(1, cache.LiveCount);
        Assert.True(cache.TryGetValue("a", out var value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void Stale_entries_are_dropped_once_more_than_the_limit_are_stored()
    {
        var cache = new GenerationalCache<int, int>(8);
        for (var round = 0; round < 50; round++)
        {
            for (var i = 0; i < 5; i++)
                cache.Set(round * 5 + i, i);
            cache.Invalidate();
            Assert.True(cache.StoredCount <= 8, $"stored {cache.StoredCount} entries after round {round}");
        }
    }

    [Fact]
    public void Live_entries_survive_while_the_cache_is_not_invalidated()
    {
        var cache = new GenerationalCache<int, int>(8);
        for (var i = 0; i < 100; i++)
            cache.Set(i, i);

        Assert.Equal(100, cache.LiveCount);
        Assert.True(cache.TryGetValue(57, out var value));
        Assert.Equal(57, value);
    }
}
