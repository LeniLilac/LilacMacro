using System.Windows.Media.Imaging;

namespace LilacMacro.App.DeepDebugViewer;

internal sealed class DeepDebugFrameCache
{
    public const long DefaultBudgetBytes = 1024L * 1024 * 1024;
    private readonly DeepDebugArchive _archive;
    private readonly object _gate = new();
    private readonly Dictionary<int, CacheItem> _items = [];
    private readonly LinkedList<int> _lru = [];
    private long _currentBytes;

    public DeepDebugFrameCache(DeepDebugArchive archive) => _archive = archive;
    public long CurrentBytes { get { lock (_gate) return _currentBytes; } }
    public int Count { get { lock (_gate) return _items.Count; } }

    public async Task<BitmapSource> GetAsync(int frameIndex, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(frameIndex, out CacheItem? cached))
            {
                Touch(cached);
                return cached.Bitmap;
            }
        }
        byte[] bytes = await _archive.ReadFrameBytesAsync(_archive.Frames[frameIndex], cancellationToken);
        BitmapImage bitmap = new();
        using (MemoryStream stream = new(bytes, writable: false))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();
        long cost = Math.Max(bytes.LongLength, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4);
        lock (_gate)
        {
            if (_items.TryGetValue(frameIndex, out CacheItem? existing)) return existing.Bitmap;
            LinkedListNode<int> node = _lru.AddFirst(frameIndex);
            _items.Add(frameIndex, new(bitmap, cost, node));
            _currentBytes += cost;
            Trim();
        }
        return bitmap;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _lru.Clear();
            _currentBytes = 0;
        }
    }

    private void Touch(CacheItem item)
    {
        _lru.Remove(item.Node);
        _lru.AddFirst(item.Node);
    }

    private void Trim()
    {
        while (_currentBytes > DefaultBudgetBytes && _lru.Last is { } last)
        {
            int key = last.Value;
            _lru.RemoveLast();
            if (!_items.Remove(key, out CacheItem? removed)) continue;
            _currentBytes -= removed.Cost;
        }
    }

    private sealed record CacheItem(BitmapSource Bitmap, long Cost, LinkedListNode<int> Node);
}
