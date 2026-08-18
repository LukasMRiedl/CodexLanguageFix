using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexLanguageFix.Core;

public sealed class LunaProtectedText
{
    private static readonly Regex AnyPlaceholder = new(
        @"\u27e6CLF_PROTECTED_[A-F0-9]+_\d{4}\u27e7",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IReadOnlyList<Entry> _entries;

    private LunaProtectedText(string text, IReadOnlyList<Entry> entries)
    {
        Text = text;
        _entries = entries;
    }

    public string Text { get; }

    public static LunaProtectedText Create(string original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var spans = ProtectedSpanDetector.Detect(original);
        if (spans.Count == 0)
        {
            return new LunaProtectedText(original, []);
        }

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        while (original.Contains($"CLF_PROTECTED_{nonce}_", StringComparison.Ordinal))
        {
            nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        }

        var builder = new StringBuilder(original.Length);
        var entries = new List<Entry>(spans.Count);
        var cursor = 0;
        for (var index = 0; index < spans.Count; index++)
        {
            var span = spans[index];
            builder.Append(original, cursor, span.Start - cursor);
            var placeholder = $"\u27e6CLF_PROTECTED_{nonce}_{index + 1:0000}\u27e7";
            builder.Append(placeholder);
            entries.Add(new Entry(placeholder, original.Substring(span.Start, span.Length)));
            cursor = span.End;
        }

        builder.Append(original, cursor, original.Length - cursor);
        return new LunaProtectedText(builder.ToString(), entries);
    }

    public bool TryRestore(string corrected, out string restored)
    {
        ArgumentNullException.ThrowIfNull(corrected);
        restored = string.Empty;

        var matches = AnyPlaceholder.Matches(corrected);
        if (matches.Count != _entries.Count)
        {
            return false;
        }

        for (var index = 0; index < _entries.Count; index++)
        {
            if (!string.Equals(matches[index].Value, _entries[index].Placeholder, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var value = corrected;
        foreach (var entry in _entries)
        {
            if (CountOccurrences(value, entry.Placeholder) != 1)
            {
                return false;
            }

            value = value.Replace(entry.Placeholder, entry.Original, StringComparison.Ordinal);
        }

        restored = value;
        return true;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var start = 0; (start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0; start += value.Length)
        {
            count++;
        }

        return count;
    }

    private sealed record Entry(string Placeholder, string Original);
}
