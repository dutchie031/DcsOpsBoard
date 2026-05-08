using System;

namespace _2dTileExporter.Helpers;


public sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map = new(capacity);
    private readonly LinkedList<(TKey key, TValue value)> _list = new();
    private readonly object _lock = new();

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.value;
                return true;
            }
        }
        value = default;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }

            var node = _list.AddFirst((key, value));
            _map[key] = node;

            if (_map.Count > capacity)
            {
                var lru = _list.Last!;
                _list.RemoveLast();
                _map.Remove(lru.Value.key);
            }
        }
    }
}