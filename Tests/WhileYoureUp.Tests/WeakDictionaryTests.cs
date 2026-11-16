using System.Runtime.CompilerServices;
using WhileYoureUp;
using Xunit;

namespace WhileYoureUp.Tests;

public class WeakDictionaryTests
{
    class Key
    {
        public readonly int id;
        public Key(int id) => this.id = id;
    }

    class IdKey : Key
    {
        public IdKey(int id) : base(id) { }
        public override bool Equals(object obj) => obj is IdKey other && other.id == id;
        public override int GetHashCode() => id;
    }

    [Fact]
    public void Stored_value_is_returned_for_the_same_key()
    {
        var dict = new WeakDictionary<Key, string>(8);
        var key = new Key(1);
        dict[key] = "one";

        Assert.True(dict.TryGetValue(key, out var value));
        Assert.Equal("one", value);
        Assert.Equal("one", dict[key]);
    }

    [Fact]
    public void Missing_key_is_not_found()
    {
        var dict = new WeakDictionary<Key, string>(8);

        Assert.False(dict.TryGetValue(new Key(1), out var value));
        Assert.Null(value);
        Assert.False(dict.ContainsKey(new Key(1)));
    }

    [Fact]
    public void Reading_a_missing_key_through_the_indexer_throws()
    {
        var dict = new WeakDictionary<Key, string>(8);

        Assert.Throws<KeyNotFoundException>(() => dict[new Key(1)]);
    }

    [Fact]
    public void Setting_the_indexer_twice_replaces_the_value()
    {
        var dict = new WeakDictionary<Key, string>(8);
        var key = new Key(1);
        dict[key] = "one";
        dict[key] = "uno";

        Assert.Equal("uno", dict[key]);
        Assert.Single(dict);
    }

    [Fact]
    public void Adding_a_present_key_throws()
    {
        var dict = new WeakDictionary<Key, string>(8);
        var key = new Key(1);
        dict.Add(key, "one");

        Assert.Throws<ArgumentException>(() => dict.Add(key, "again"));
    }

    [Fact]
    public void Keys_are_matched_with_their_own_equality()
    {
        var dict = new WeakDictionary<IdKey, string>(8);
        var first = new IdKey(7);
        dict[first] = "seven";

        Assert.Equal("seven", dict[new IdKey(7)]);
    }

    [Fact]
    public void Removing_a_key_reports_whether_it_was_present()
    {
        var dict = new WeakDictionary<Key, string>(8);
        var key = new Key(1);
        dict[key] = "one";

        Assert.True(dict.Remove(key));
        Assert.False(dict.Remove(key));
        Assert.False(dict.ContainsKey(key));
        Assert.Empty(dict);
    }

    [Fact]
    public void Enumeration_lists_every_live_pair()
    {
        var dict = new WeakDictionary<Key, string>(8);
        var a = new Key(1);
        var b = new Key(2);
        dict[a] = "a";
        dict[b] = "b";

        var pairs = dict.ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("a", pairs[a]);
        Assert.Equal("b", pairs[b]);
        Assert.Equal(2, dict.Keys.Count);
        Assert.Equal(2, dict.Values.Count);
    }

    [Fact]
    public void GetValueSafe_returns_the_value_or_the_type_default()
    {
        var dict = new WeakDictionary<Key, string>(8);
        var key = new Key(1);
        dict[key] = "one";

        Assert.Equal("one", dict.GetValueSafe(key));
        Assert.Null(dict.GetValueSafe(new Key(2)));
        Assert.Null(dict.GetValueSafe(null));
    }

    [Fact]
    public void Clear_empties_the_dictionary()
    {
        var dict = new WeakDictionary<Key, string>(8);
        dict[new Key(1)] = "one";
        dict.Clear();

        Assert.Empty(dict);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void AddTemporaryKeys(WeakDictionary<Key, string> dict, int count)
    {
        for (var i = 0; i < count; i++)
            dict[new Key(i)] = "temp";
    }

    [Fact]
    public void An_entry_disappears_once_its_key_is_collected()
    {
        var dict = new WeakDictionary<Key, string>(8);
        var survivor = new Key(100);
        dict[survivor] = "kept";
        AddTemporaryKeys(dict, 5);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Single(dict);
        Assert.Equal("kept", dict[survivor]);
        GC.KeepAlive(survivor);
    }

    [Fact]
    public void Collected_entries_are_purged_while_adding()
    {
        var dict = new WeakDictionary<Key, string>(4);
        AddTemporaryKeys(dict, 50);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var keep = new Key(1);
        dict[keep] = "new";

        Assert.Single(dict);
        GC.KeepAlive(keep);
    }
}
