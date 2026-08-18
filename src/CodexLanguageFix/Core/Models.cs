namespace CodexLanguageFix.Core;

public enum CorrectionProviderKind
{
    LanguageTool,
    Luna
}

public sealed record CorrectionProviderResult(
    string CorrectedText,
    int ChangeCount,
    CorrectionProviderKind Provider,
    int? StatusCode,
    TimeSpan Elapsed);

public sealed record CorrectionProviderHealth(CorrectionProviderKind Provider, TimeSpan Elapsed);

public sealed record CodexAccountState(bool RequiresOpenAiAuth, bool IsChatGpt);

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool Overlaps(int start, int length)
    {
        var end = start + length;
        return start < End && Start < end;
    }
}

public sealed record PromptAnnotation(string? Text = null, string? Markup = null, string? InterpretAs = null);

public sealed record AnnotatedPrompt(
    string Original,
    IReadOnlyList<TextSpan> ProtectedSpans,
    IReadOnlyList<PromptAnnotation> Annotations);

public sealed record LanguageToolMatch(
    int Offset,
    int Length,
    IReadOnlyList<string> Replacements,
    string RuleId,
    string CategoryId,
    string IssueType,
    double? Confidence,
    int ResponseIndex);

public sealed record LanguageToolCheckResult(
    IReadOnlyList<LanguageToolMatch> Matches,
    int StatusCode,
    TimeSpan Elapsed);

public sealed record AppliedCorrection(int Offset, int Length, string Replacement, string RuleId);

public sealed record CorrectionOutcome(string CorrectedText, IReadOnlyList<AppliedCorrection> Corrections)
{
    public bool Changed => Corrections.Count > 0;
}

public class LanguageFixException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class RateLimitException(string message, TimeSpan retryAfter) : LanguageFixException(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public sealed class CodexAppServerException(string message, Exception? innerException = null) : LanguageFixException(message, innerException);
