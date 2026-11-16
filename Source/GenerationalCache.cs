using System.Collections.Generic;

namespace WhileYoureUp;

sealed class GenerationalCache<TKey, TValue>
{
    readonly Dictionary<TKey, (int generation, TValue value)> entries;
    readonly int                                              maxStored;
    int                                                       generation = 1;

    public GenerationalCache(int maxStored) {
        this.maxStored = maxStored;
        entries        = new Dictionary<TKey, (int, TValue)>(maxStored);
    }

    public int LiveCount   { get; private set; }
    public int StoredCount => entries.Count;

    public bool TryGetValue(TKey key, out TValue value) {
        if (entries.TryGetValue(key, out var entry) && entry.generation == generation) {
            value = entry.value;
            return true;
        }
        value = default;
        return false;
    }

    public void Set(TKey key, TValue value) {
        if (!entries.TryGetValue(key, out var entry) || entry.generation != generation)
            LiveCount++;
        entries[key] = (generation, value);
    }

    public void Invalidate() {
        generation++;
        LiveCount = 0;
        if (entries.Count > maxStored)
            entries.Clear();
    }
}
