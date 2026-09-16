using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Core;

// Nur eine Zuordnung; Text und Editoridentität stammen immer aus der aktuellen Erfassung.
internal sealed class ComposerLayoutCache
{
    private ComposerSnapshot? _snapshot;
    private uint _dpi;

    public void Clear() => _snapshot = null;

    public void Store(ComposerSnapshot snapshot, uint dpi)
    {
        _snapshot = snapshot;
        _dpi = dpi;
    }

    public ComposerSnapshot? TryApply(ComposerSnapshot current, uint dpi)
    {
        if (_snapshot is not { } previous || dpi == 0 || dpi != _dpi
            || !previous.SameEditor(current) || previous.EditorBounds != current.EditorBounds
            || !string.Equals(previous.Text, current.Text, StringComparison.Ordinal))
        {
            Clear();
            return null;
        }

        return current with
        {
            Bounds = previous.Bounds,
            RightControlBounds = previous.RightControlBounds,
            OccupiedBounds = previous.OccupiedBounds
        };
    }
}
