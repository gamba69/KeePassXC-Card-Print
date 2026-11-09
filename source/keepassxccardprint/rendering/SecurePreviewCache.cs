namespace KeePassXCCardPrint.Rendering;

/// <summary>Bounded RAM-only PNG LRU. Cache entries may contain credentials!
/// No filesystem access, log output, or global/static persistence. Replaced and
/// evicted byte arrays are overwritten before releasing their references.</summary>
internal sealed class SecurePreviewCache : IDisposable
{
    private sealed record Entry(string Key,byte[] Data);
    private readonly object _gate=new();
    private readonly int _maxBytes;
    private readonly LinkedList<Entry> _lru=new();
    private readonly Dictionary<string,LinkedListNode<Entry>> _entries=new(StringComparer.Ordinal);
    private int _bytes;

    internal SecurePreviewCache(int maxBytes=48*1024*1024)
    {
        _maxBytes=Math.Clamp(maxBytes,1024*1024,128*1024*1024);
    }
    internal bool TryGet(string key,out byte[] png)
    {
        lock(_gate)
        {
            if(_entries.TryGetValue(key,out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                // The caller must clear this private copy after decoding.
                png=(byte[])node.Value.Data.Clone();
                return true;
            }
        }
        png=[];
        return false;
    }
    internal void Put(string key,byte[] png)
    {
        if(png.Length==0 || png.Length>_maxBytes/3)return;
        lock(_gate)
        {
            if(_entries.TryGetValue(key,out var old))Remove(old);
            var copy=(byte[])png.Clone();
            var node=_lru.AddFirst(new Entry(key,copy));
            _entries.Add(key,node);
            _bytes+=copy.Length;
            while(_bytes>_maxBytes && _lru.Last is { } last)Remove(last);
        }
    }
    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _lru.Remove(node);
        _bytes-=node.Value.Data.Length;
        Array.Clear(node.Value.Data);
    }
    internal void Clear()
    {
        lock(_gate)
        {
            while(_lru.Last is { } last)Remove(last);
        }
    }
    public void Dispose()=>Clear();
}
