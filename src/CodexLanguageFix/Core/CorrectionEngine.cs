using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Core;

public sealed class CorrectionEngine : ICorrectionEngine
{
    public AnnotatedPrompt Annotate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var protectedSpans = ProtectedSpanDetector.Detect(text);
        var annotations = new List<PromptAnnotation>();
        var cursor = 0;

        foreach (var span in protectedSpans)
        {
            if (span.Start > cursor)
            {
                annotations.Add(new PromptAnnotation(Text: text[cursor..span.Start]));
            }

            annotations.Add(new PromptAnnotation(Markup: text.Substring(span.Start, span.Length), InterpretAs: " "));
            cursor = span.End;
        }

        if (cursor < text.Length)
        {
            annotations.Add(new PromptAnnotation(Text: text[cursor..]));
        }

        if (annotations.Count == 0)
        {
            annotations.Add(new PromptAnnotation(Text: text));
        }

        return new AnnotatedPrompt(text, protectedSpans, annotations);
    }

    public CorrectionOutcome Apply(string original, AnnotatedPrompt prompt, IReadOnlyList<LanguageToolMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(matches);

        if (!string.Equals(original, prompt.Original, StringComparison.Ordinal))
        {
            throw new ArgumentException("Der annotierte Prompt gehört nicht zum angegebenen Originaltext.", nameof(prompt));
        }

        var candidates = matches
            .Where(match => match.Offset >= 0 && match.Offset <= original.Length
                && match.Length >= 0 && match.Length <= original.Length - match.Offset)
            .Where(match => match.Replacements.Count > 0)
            .Where(match => !prompt.ProtectedSpans.Any(span => span.Overlaps(match.Offset, Math.Max(match.Length, 1))))
            .Select(match => new Candidate(match, match.Replacements[0]))
            .Where(candidate => !string.Equals(
                original.Substring(candidate.Match.Offset, candidate.Match.Length),
                candidate.Replacement,
                StringComparison.Ordinal))
            .Where(candidate => TextStructure.IsPreserved(original,
                original.Remove(candidate.Match.Offset, candidate.Match.Length)
                    .Insert(candidate.Match.Offset, candidate.Replacement)))
            .OrderByDescending(candidate => candidate.Match.Confidence ?? double.MinValue)
            .ThenByDescending(candidate => candidate.Match.Length)
            .ThenBy(candidate => candidate.Match.ResponseIndex)
            .ToArray();

        var selected = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            if (selected.All(existing => !Overlaps(existing.Match, candidate.Match)))
            {
                selected.Add(candidate);
            }
        }

        var corrected = original;
        var applied = new List<AppliedCorrection>();
        foreach (var candidate in selected.OrderByDescending(candidate => candidate.Match.Offset))
        {
            var updated = corrected.Remove(candidate.Match.Offset, candidate.Match.Length)
                .Insert(candidate.Match.Offset, candidate.Replacement);
            if (!TextStructure.IsPreserved(original, updated))
            {
                continue;
            }

            corrected = updated;
            applied.Add(new AppliedCorrection(
                candidate.Match.Offset,
                candidate.Match.Length,
                candidate.Replacement,
                candidate.Match.RuleId));
        }

        applied.Reverse();
        return new CorrectionOutcome(corrected, applied);
    }

    private static bool Overlaps(LanguageToolMatch left, LanguageToolMatch right)
    {
        var leftLength = Math.Max(left.Length, 1);
        var rightLength = Math.Max(right.Length, 1);
        return left.Offset < right.Offset + rightLength && right.Offset < left.Offset + leftLength;
    }

    private sealed record Candidate(LanguageToolMatch Match, string Replacement);
}
