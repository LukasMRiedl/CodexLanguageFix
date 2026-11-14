using CodexLanguageFix.Contracts;
using System.Windows;

namespace CodexLanguageFix.Core;

// Nur eine Zuordnung; Text und Editoridentität stammen immer aus der aktuellen Erfassung.
internal sealed class ComposerLayoutCache
{
    private Layout? _snapshot;
    private uint _dpi;

    public void Clear() => _snapshot = null;

    public void Store(ComposerSnapshot snapshot, uint dpi)
    {
        _snapshot = new(snapshot.RuntimeId.ToArray(), snapshot.ProcessId, snapshot.Host, snapshot.HostWindow,
            snapshot.Text, snapshot.FieldCategory, snapshot.EditorBounds, snapshot.Bounds,
            snapshot.RightControlBounds, snapshot.OccupiedBounds?.ToArray(), snapshot.PlacementBounds,
            snapshot.EditorTextBounds?.ToArray());
        _dpi = dpi;
    }

    public ComposerSnapshot? TryApply(ComposerSnapshot current, uint dpi)
    {
        if (_snapshot is not { } previous || dpi == 0 || dpi != _dpi
            || previous.RuntimeId.Length == 0 || !previous.RuntimeId.SequenceEqual(current.RuntimeId)
            || previous.ProcessId != current.ProcessId || previous.Host != current.Host
            || previous.HostWindow != current.HostWindow || previous.EditorBounds != current.EditorBounds
            || previous.FieldCategory != current.FieldCategory
            || !string.Equals(previous.Text, current.Text, StringComparison.Ordinal))
        {
            Clear();
            return null;
        }

        return current with
        {
            Bounds = previous.Bounds,
            RightControlBounds = previous.RightControlBounds,
            OccupiedBounds = previous.OccupiedBounds,
            PlacementBounds = previous.PlacementBounds,
            EditorTextBounds = previous.EditorTextBounds
        };
    }

    // Geometry never owns an AutomationElement, text pattern, or native editor handle.
    private sealed record Layout(int[] RuntimeId, int ProcessId, ComposerHost Host, nint HostWindow,
        string Text, string FieldCategory, Rect? EditorBounds, Rect Bounds, Rect? RightControlBounds,
        IReadOnlyList<Rect>? OccupiedBounds, Rect? PlacementBounds, IReadOnlyList<Rect>? EditorTextBounds);
}
