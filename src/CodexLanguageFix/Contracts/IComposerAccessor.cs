using System.Windows;

namespace CodexLanguageFix.Contracts;

public interface IComposerAccessor
{
    ComposerSnapshot? TryCaptureFocusedComposer();
    ComposerSnapshot? TryRefresh(ComposerSnapshot snapshot);
    bool TryReplace(ComposerSnapshot snapshot, string expectedText, string replacement);
}

public enum ComposerHost
{
    Codex,
    Antigravity
}

public sealed record ComposerSnapshot(
    object NativeElement,
    int[] RuntimeId,
    string Text,
    Rect Bounds,
    int ProcessId,
    Rect? RightControlBounds = null,
    ComposerHost Host = ComposerHost.Codex);
