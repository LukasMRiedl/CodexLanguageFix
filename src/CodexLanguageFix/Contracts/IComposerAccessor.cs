using System.Windows;

namespace CodexLanguageFix.Contracts;

public interface IComposerAccessor
{
    ComposerSnapshot? TryCaptureFocusedComposer();
    ComposerSnapshot? TryPollFocusedComposer();
    void InvalidateLayout();
    ComposerSnapshot? TryRefresh(ComposerSnapshot snapshot);
    bool TryReplace(ComposerSnapshot snapshot, string expectedText, string replacement);
    ComposerWriteResult? LastWriteResult => null;
}

public enum ComposerHost
{
    Codex,
    Antigravity,
    Hermes
}

public sealed record ComposerSnapshot(
    object NativeElement,
    int[] RuntimeId,
    string Text,
    Rect Bounds,
    int ProcessId,
    Rect? RightControlBounds = null,
    ComposerHost Host = ComposerHost.Codex,
    nint HostWindow = default,
    Rect? EditorBounds = null,
    IReadOnlyList<Rect>? OccupiedBounds = null,
    ComposerReadMethod ReadMethod = ComposerReadMethod.Unknown)
{
    public bool SameEditor(ComposerSnapshot? other) => other is not null
        && Host == other.Host && ProcessId == other.ProcessId && HostWindow == other.HostWindow
        && RuntimeId.Length > 0 && RuntimeId.SequenceEqual(other.RuntimeId);
}

public enum ComposerReadMethod { Unknown, ValuePattern, TextPattern }
public enum ComposerWriteState { Unchanged, Applied, PartiallyApplied }
public sealed record ComposerWriteResult(
    ComposerWriteState State,
    string OriginalText,
    string? ObservedText,
    int AppliedChanges,
    string Status);
