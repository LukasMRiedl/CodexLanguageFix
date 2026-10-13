using System.Text.RegularExpressions;

namespace CodexLanguageFix.Core;

internal static class RepeatedSentenceGuard
{
    private static readonly Regex Boundaries = new(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static bool HasReducedUnchangedRepetitions(string original, string corrected)
    {
        var before = Counts(original);
        var after = Counts(corrected);
        return before.Any(entry => entry.Value > 1 && after.TryGetValue(entry.Key, out var remaining)
            && remaining < entry.Value);
    }

    private static Dictionary<string, int> Counts(string text) => Boundaries.Split(text)
        .Select(sentence => sentence.Trim()).Where(sentence => sentence.Length > 0)
        .GroupBy(sentence => sentence, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
