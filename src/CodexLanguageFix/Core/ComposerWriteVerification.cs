using System.Diagnostics;

namespace CodexLanguageFix.Core;

internal enum ComposerWriteCheck
{
    Exact,
    TextMismatch,
    EditorChanged
}

internal static class ComposerWriteVerification
{
    public static ComposerWriteCheck WaitForExact(
        string expected, Func<string?> readText, Func<bool> isEditorCurrent)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (!isEditorCurrent()) return ComposerWriteCheck.EditorChanged;
            var actual = readText();
            if (!isEditorCurrent()) return ComposerWriteCheck.EditorChanged;
            if (string.Equals(actual, expected, StringComparison.Ordinal)) return ComposerWriteCheck.Exact;

            var remaining = TimeSpan.FromSeconds(2) - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) return ComposerWriteCheck.TextMismatch;
            Thread.Sleep((int)Math.Ceiling(Math.Min(remaining.TotalMilliseconds, 25)));
            if (stopwatch.Elapsed >= TimeSpan.FromSeconds(2)) return ComposerWriteCheck.TextMismatch;
        }
    }
}
