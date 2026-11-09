namespace KeePassXCCardPrint.Rendering;

/// <summary>
/// Small RAM-only LRU of immutable vector display lists. Each cached scene
/// owns a reference; current/previous views and queued Avalonia render ops own
/// independent references so cache eviction never invalidates the display.
/// No PNG generation, disk access, secret logging or image duplication.
/// </summary>
internal sealed class SecurePreviewSceneCache : IDisposable
{
    private sealed record Entry(string Key,SkiaPreviewScene Scene);
    private readonly object _sync=new();
    private readonly LinkedList<Entry> _lru=new();
    private readonly Dictionary<string,LinkedListNode<Entry>> _lookup=new(StringComparer.Ordinal);
    private const int MaxScenes=12;

    internal bool TryGet(string key,out SkiaPreviewScene? scene)
    {
        lock(_sync)
        {
            if(_lookup.TryGetValue(key,out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                scene=node.Value.Scene.Retain();
                return true;
            }
        }
        scene=null;
        return false;
    }

    internal void Put(string key,SkiaPreviewScene scene)
    {
        lock(_sync)
        {
            if(_lookup.TryGetValue(key,out var existing))Remove(existing);
            var node=_lru.AddFirst(new Entry(key,scene.Retain()));
            _lookup.Add(key,node);
            while(_lookup.Count>MaxScenes && _lru.Last is { } oldest)Remove(oldest);
        }
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _lookup.Remove(node.Value.Key);
        _lru.Remove(node);
        node.Value.Scene.Dispose();
    }

    internal void Clear()
    {
        lock(_sync)
        {
            while(_lru.Last is { } node)Remove(node);
        }
    }
    public void Dispose()=>Clear();
}
