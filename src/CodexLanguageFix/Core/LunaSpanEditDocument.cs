using System.Text;
using System.Text.RegularExpressions;

namespace CodexLanguageFix.Core;

/// <summary>
/// Lokale, verlustfreie Anwendung eines kompakten Span-Edit-Protokolls.
/// Offsets sind .NET-String-Offsets (UTF-16-Codeeinheiten), da die Luna-
/// Eingabe und die anschließende Validierung dieselbe Darstellung verwenden.
/// </summary>
internal static class LunaSpanEditDocument
{
    private static readonly Regex ProtectedMarker = new(
        @"⟦CLF_PROTECTED_[A-F0-9]+_\d{4}⟧",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryApply(
        string original,
        IReadOnlyList<LunaSpanEdit> edits,
        out string corrected,
        out int changeCount)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edits);

        corrected = string.Empty;
        changeCount = 0;

        var protectedMarkers = ProtectedMarker.Matches(original)
            .Cast<Match>()
            .Select(match => new TextSpan(match.Index, match.Length))
            .ToArray();
        var builder = new StringBuilder(original.Length);
        var cursor = 0;
        var previousStart = -1;
        var previousEnd = -1;

        foreach (var edit in edits)
        {
            if (edit is null
                || edit.Text is null
                || edit.Start < 0
                || edit.Length < 0
                || edit.Start > original.Length
                || edit.Length > original.Length - edit.Start
                || (previousStart >= 0
                    && (edit.Start < previousEnd || edit.Start == previousStart))
                || edit.Start < cursor
                || !IsSafeBoundary(original, edit.Start)
                || !IsSafeBoundary(original, edit.Start + edit.Length)
                || IntersectsProtectedMarker(edit.Start, edit.Length, protectedMarkers)
                || ContainsProtectedMarker(edit.Text)
                || HasUnexpectedControlCharacters(edit.Text)
                || ContainsUnpairedSurrogate(edit.Text))
            {
                corrected = string.Empty;
                changeCount = 0;
                return false;
            }

            var source = original.AsSpan(edit.Start, edit.Length);
            if (source.SequenceEqual(edit.Text.AsSpan()))
            {
                // Ein unverändertes Edit ist ein Protokollfehler. Ein unveränderter
                // Text wird ausschließlich durch eine leere Edit-Liste dargestellt.
                corrected = string.Empty;
                changeCount = 0;
                return false;
            }

            builder.Append(original, cursor, edit.Start - cursor);
            builder.Append(edit.Text);
            cursor = edit.Start + edit.Length;
            previousStart = edit.Start;
            previousEnd = cursor;
            changeCount++;
        }

        builder.Append(original, cursor, original.Length - cursor);
        corrected = builder.ToString();
        return !HasUnexpectedControlCharacters(corrected)
            && !ContainsUnpairedSurrogate(corrected)
            && HasStableLineBreaks(original, corrected)
            && corrected.Length <= Math.Max(1_000, original.Length * 2 + 500);
    }

    private static bool IntersectsProtectedMarker(int start, int length, IReadOnlyList<TextSpan> markers)
    {
        var end = start + length;
        foreach (var marker in markers)
        {
            if (length == 0)
            {
                if (start >= marker.Start && start <= marker.End)
                {
                    return true;
                }
            }
            else if (start < marker.End && end > marker.Start)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsProtectedMarker(string text) => ProtectedMarker.IsMatch(text);

    private static bool IsSafeBoundary(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return true;
        }

        // Niemals ein UTF-16-Surrogatpaar oder CRLF teilen. Dadurch bleiben
        // Unicode-Zeichen und Zeilenumbrüche auch bei modellgenerierten Offsets
        // bytegenau rekonstruierbar.
        return !(char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]))
            && !(text[index - 1] == '\r' && text[index] == '\n');
    }

    private static bool HasUnexpectedControlCharacters(string text) =>
        text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));

    private static bool HasStableLineBreaks(string original, string corrected)
    {
        var sourceBreaks = GetLineBreaks(original);
        var correctedBreaks = GetLineBreaks(corrected);
        return sourceBreaks.SequenceEqual(correctedBreaks, StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetLineBreaks(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    yield return "\r\n";
                    index++;
                }
                else
                {
                    yield return "\r";
                }
            }
            else if (text[index] == '\n')
            {
                yield return "\n";
            }
        }
    }

    private static bool ContainsUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                {
                    return true;
                }

                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct TextSpan(int Start, int Length)
    {
        public int End => Start + Length;
    }
}

internal sealed record LunaSpanEdit(int Start, int Length, string? Text);
