namespace LilacMacro.App.Views;

internal sealed class RunLogBuffer
{
    internal const int DefaultCapacity = 1_000;

    private readonly object _gate = new();
    private readonly Queue<string> _entries = new();
    private readonly int _capacity;
    private long _version;
    private long _presentedVersion;

    internal RunLogBuffer(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    internal void Add(string entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(entry);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity) _entries.Dequeue();
            _version++;
        }
    }

    internal bool TryGetUpdatedText(out string text)
    {
        lock (_gate)
        {
            if (_version == _presentedVersion)
            {
                text = string.Empty;
                return false;
            }

            text = string.Join(Environment.NewLine, _entries);
            _presentedVersion = _version;
            return true;
        }
    }
}
