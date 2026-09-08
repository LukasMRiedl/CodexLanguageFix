using System.Diagnostics;

namespace CodexLanguageFix.Core;

internal enum ComposerSelectionCheck
{
    Exact,
    CountMismatch,
    StartMismatch,
    EndMismatch,
    TextMismatch,
    EditorChanged,
    SourceChanged
}

internal static class ComposerSelectionVerification
{
    public static ComposerSelectionCheck WaitForExact(Func<ComposerSelectionCheck> check)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = check();
        while (result is ComposerSelectionCheck.CountMismatch or ComposerSelectionCheck.StartMismatch
            or ComposerSelectionCheck.EndMismatch or ComposerSelectionCheck.TextMismatch)
        {
            var remaining = TimeSpan.FromMilliseconds(500) - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) return result;
            Thread.Sleep((int)Math.Ceiling(Math.Min(remaining.TotalMilliseconds, 25)));
            if (stopwatch.Elapsed >= TimeSpan.FromMilliseconds(500)) return result;
            result = check();
        }

        return result;
    }

    public static string Status(ComposerSelectionCheck check) => check switch
    {
        ComposerSelectionCheck.Exact => "ok",
        ComposerSelectionCheck.CountMismatch => "selection_count_mismatch",
        ComposerSelectionCheck.StartMismatch => "selection_start_mismatch",
        ComposerSelectionCheck.EndMismatch => "selection_end_mismatch",
        ComposerSelectionCheck.TextMismatch => "selection_text_mismatch",
        ComposerSelectionCheck.EditorChanged => "invalid_identity_after_selection",
        ComposerSelectionCheck.SourceChanged => "before_mismatch_after_selection",
        _ => throw new ArgumentOutOfRangeException(nameof(check))
    };
}
