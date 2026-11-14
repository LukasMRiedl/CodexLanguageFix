using System.Windows;

namespace CodexLanguageFix.Contracts;

public interface IComposerAccessor
{
    ComposerSnapshot? TryCaptureFocusedComposer();
    ComposerSnapshot? TryCaptureFocusedComposerWithoutLayout(bool requireKeyboardFocus = false);
    ComposerSnapshot? TryPollFocusedComposer();
    void InvalidateLayout();
    ComposerSnapshot? TryRefresh(ComposerSnapshot snapshot);
    bool TryReplace(ComposerSnapshot snapshot, string expectedText, string replacement);
    ComposerWriteResult? LastWriteResult => null;
    bool LastCaptureWasKeyboardFocused => false;
    string LastCaptureStatus => "unknown";
    string LastWriteStatus => "unknown";
    ComposerHost? LastCaptureHost => null;
    string? LastFieldCategory => null;
}

public enum ComposerHost
{
    Codex,
    Antigravity,
    Hermes,
    Windows
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
    ComposerReadMethod ReadMethod = ComposerReadMethod.Unknown,
    Rect? PlacementBounds = null,
    string FieldCategory = "chat",
    IReadOnlyList<Rect>? EditorTextBounds = null)
{
    public bool SameEditor(ComposerSnapshot? other) => other is not null
        && Host == other.Host && ProcessId == other.ProcessId && HostWindow == other.HostWindow
        && RuntimeId.Length > 0 && RuntimeId.SequenceEqual(other.RuntimeId);
}

// Identity checks do not need to keep the editor's UIA provider alive.
internal sealed record ComposerEditorIdentity(int[] RuntimeId, int ProcessId, ComposerHost Host, nint HostWindow)
{
    internal static ComposerEditorIdentity From(ComposerSnapshot snapshot) =>
        new(snapshot.RuntimeId.ToArray(), snapshot.ProcessId, snapshot.Host, snapshot.HostWindow);

    internal bool SameEditor(ComposerSnapshot? other) => other is not null
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
