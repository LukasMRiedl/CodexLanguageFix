namespace CodexLanguageFix.Core;

public enum CorrectionProviderKind
{
    Luna = 1
}

public sealed record CorrectionProviderResult(
    string CorrectedText,
    int ChangeCount,
    CorrectionProviderKind Provider,
    int? StatusCode,
    TimeSpan Elapsed)
{
    public bool CacheHit { get; init; }
    public LunaCorrectionExecution? LunaExecution { get; init; }
}

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

public class LanguageFixException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class CodexAppServerException(string message, Exception? innerException = null) : LanguageFixException(message, innerException);
