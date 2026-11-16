using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace WhileYoureUp;

public class WeakDictionary<TKey, TValue> : IDictionary<TKey, TValue> where TKey : class
{
    readonly struct WeakKey : IEquatable<WeakKey>
    {
        readonly WeakReference reference;
        readonly TKey          probe;
        readonly int           hash;

        public WeakKey(TKey key) {
            reference = new WeakReference(key);
            probe     = null;
            hash      = EqualityComparer<TKey>.Default.GetHashCode(key);
        }

        WeakKey(TKey key, bool _) {
            reference = null;
            probe     = key;
            hash      = EqualityComparer<TKey>.Default.GetHashCode(key);
        }

        public static WeakKey Probe(TKey key) => new(key, true);

        public TKey Target  => reference is null ? probe : (TKey)reference.Target;
        public bool IsAlive => reference is null || reference.IsAlive;

        public bool Equals(WeakKey other) {
            if (reference is not null && ReferenceEquals(reference, other.reference)) return true;
            if (hash != other.hash) return false;
            var a = Target;
            var b = other.Target;
            return a is not null && b is not null && EqualityComparer<TKey>.Default.Equals(a, b);
        }

        public override bool Equals(object obj) => obj is WeakKey other && Equals(other);
        public override int  GetHashCode()      => hash;
    }

    readonly Dictionary<WeakKey, TValue> entries;
    int                                  purgeThreshold;

    public WeakDictionary(int capacity = 0) {
        entries        = new Dictionary<WeakKey, TValue>(capacity);
        purgeThreshold = Math.Max(capacity, 8);
    }

    void PurgeDead() {
        List<WeakKey> dead = null;
        foreach (var key in entries.Keys)
            if (!key.IsAlive)
                (dead ??= new List<WeakKey>()).Add(key);
        if (dead is not null)
            foreach (var key in dead)
                entries.Remove(key);
        purgeThreshold = Math.Max(entries.Count * 2, 8);
    }

    void PurgeIfDue() {
        if (entries.Count >= purgeThreshold)
            PurgeDead();
    }

    public int  Count      => LiveEntries().Count();
    public bool IsReadOnly => false;

    IEnumerable<KeyValuePair<TKey, TValue>> LiveEntries() {
        foreach (var pair in entries) {
            var key = pair.Key.Target;
            if (key is not null)
                yield return new KeyValuePair<TKey, TValue>(key, pair.Value);
        }
    }

    public TValue this[TKey key] {
        get {
            if (TryGetValue(key, out var value)) return value;
            throw new KeyNotFoundException();
        }
        set {
            if (key is null) throw new ArgumentNullException(nameof(key));
            PurgeIfDue();
            var probe = WeakKey.Probe(key);
            if (entries.ContainsKey(probe))
                entries[probe] = value;
            else
                entries[new WeakKey(key)] = value;
        }
    }

    public ICollection<TKey>   Keys   => LiveEntries().Select(p => p.Key).ToList();
    public ICollection<TValue> Values => LiveEntries().Select(p => p.Value).ToList();

    public bool TryGetValue(TKey key, out TValue value) {
        if (key is null) throw new ArgumentNullException(nameof(key));
        return entries.TryGetValue(WeakKey.Probe(key), out value);
    }

    public TValue GetValueSafe(TKey key) => key is not null && TryGetValue(key, out var value) ? value : default;

    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    public void Add(TKey key, TValue value) {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (ContainsKey(key)) throw new ArgumentException("An item with the same key has already been added.", nameof(key));
        PurgeIfDue();
        entries.Add(new WeakKey(key), value);
    }

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public bool Remove(TKey key) {
        if (key is null) throw new ArgumentNullException(nameof(key));
        return entries.Remove(WeakKey.Probe(key));
    }

    public bool Remove(KeyValuePair<TKey, TValue> item) =>
        TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value) && Remove(item.Key);

    public bool Contains(KeyValuePair<TKey, TValue> item) =>
        TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);

    public void Clear() => entries.Clear();

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
        foreach (var pair in LiveEntries())
            array[arrayIndex++] = pair;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => LiveEntries().ToList().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()                         => GetEnumerator();
}
