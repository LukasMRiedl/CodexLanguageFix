using System.Windows;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class ComposerIdentityTests
{
    private static ComposerSnapshot Snapshot() => new(new object(), [1, 2], "Text", new Rect(0, 0, 200, 100), 42, HostWindow: 123);

    [Fact]
    public void SameTextInAnotherEditorDoesNotAuthorizeWriting()
    {
        var original = Snapshot();
        Assert.False(CorrectionCoordinator.IsUnchangedEditor(original, original with { RuntimeId = [1, 3] }));
        Assert.False(CorrectionCoordinator.IsUnchangedEditor(original, original with { ProcessId = 43 }));
        Assert.False(CorrectionCoordinator.IsUnchangedEditor(original, original with { HostWindow = 124 }));
        Assert.False(CorrectionCoordinator.IsUnchangedEditor(original, original with { Host = ComposerHost.Antigravity }));
    }

    [Fact]
    public void UserEditsIncludingFinalNewlineInvalidateRequest()
    {
        var original = Snapshot();
        Assert.True(CorrectionCoordinator.IsUnchangedEditor(original, original with { RuntimeId = [1, 2] }));
        Assert.False(CorrectionCoordinator.IsUnchangedEditor(original, original with { Text = "Text\n" }));
        Assert.False(CorrectionCoordinator.IsUnchangedEditor(original, original with { Text = "Anderer Text" }));
    }
}
