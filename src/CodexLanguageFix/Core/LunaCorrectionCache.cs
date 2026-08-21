using System.Security.Cryptography;
using System.Text;

namespace CodexLanguageFix.Core;

internal sealed class LunaCorrectionCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recency = new();
    private readonly object _gate = new();

    public LunaCorrectionCache(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public static string CreateKey(string text, string profileFingerprint)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFingerprint);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileFingerprint + "\0" + text)));
    }

    public bool TryGet(string key, out LunaCachedCorrection correction)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                correction = default!;
                return false;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            correction = node.Value.Correction;
            return true;
        }
    }

    public void Set(string key, LunaCachedCorrection correction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(correction);
        lock (_gate)
        {
            if (_entries.Remove(key, out var existing))
            {
                _recency.Remove(existing);
            }

            var node = _recency.AddFirst(new Entry(key, correction));
            _entries.Add(key, node);
            while (_entries.Count > _capacity)
            {
                var last = _recency.Last!;
                _recency.RemoveLast();
                _entries.Remove(last.Value.Key);
            }
        }
    }

    private sealed record Entry(string Key, LunaCachedCorrection Correction);
}

internal sealed record LunaCachedCorrection(string CorrectedText, int ChangeCount);
