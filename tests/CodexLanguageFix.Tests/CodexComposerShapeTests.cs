using System.Windows.Automation;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class CodexComposerShapeTests
{
    [Theory]
    [InlineData(196, 56, true)]
    [InlineData(85, 56, true)]
    [InlineData(84, 56, false)]
    [InlineData(56, 56, false)]
    public void VariableWidthModelControl_IsAcceptedAsCodexAnchor(
        double width,
        double height,
        bool expected)
    {
        Assert.Equal(
            expected,
            CodexComposerAccessor.IsCodexModelAnchor(
                new System.Windows.Rect(400, 600, width, height)));
    }

    [Fact]
    public void OnlyProseMirrorEdit_IsUsedForUnfocusedCodexComposer()
    {
        Assert.True(CodexComposerAccessor.IsComposerShapeForHost(
            ControlType.Edit,
            "ProseMirror ProseMirror-focused",
            ComposerHost.Codex));
        Assert.False(CodexComposerAccessor.IsComposerShapeForHost(
            ControlType.Edit,
            "terminal-input",
            ComposerHost.Codex));
    }

    [Fact]
    public void CompactChatSurface_ExtendsEditorBoundsToIncludeToolbar()
    {
        var editor = new System.Windows.Rect(1090, 867, 939, 45);

        Assert.True(CodexComposerAccessor.IsComposerSurfaceCandidate(
            editor,
            new System.Windows.Rect(999, 840, 1409, 98)));
        Assert.False(CodexComposerAccessor.IsComposerSurfaceCandidate(
            editor,
            new System.Windows.Rect(240, 212, 2400, 1460)));
        Assert.False(CodexComposerAccessor.IsComposerSurfaceCandidate(
            editor,
            new System.Windows.Rect(1090, 849, 939, 80)));
    }

    [Fact]
    public void CodexUsesRightmostWideControlInsteadOfPlanModeButton()
    {
        var planMode = new System.Windows.Rect(543, 1577, 169, 63);
        var model = new System.Windows.Rect(908, 1577, 303, 63);

        Assert.True(CodexComposerAccessor.IsBetterToolbarAnchor(
            model,
            planMode,
            ComposerHost.Codex));
        Assert.False(CodexComposerAccessor.IsBetterToolbarAnchor(
            planMode,
            model,
            ComposerHost.Codex));
    }

    [Fact]
    public void AntigravityKeepsLeftmostRightSideControl()
    {
        var microphone = new System.Windows.Rect(2331, 1093, 56, 57);
        var send = new System.Windows.Rect(2395, 1093, 56, 57);

        Assert.True(CodexComposerAccessor.IsBetterToolbarAnchor(
            microphone,
            send,
            ComposerHost.Antigravity));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 1)]
    [InlineData(120, 10)]
    [InlineData(20_000, 512)]
    public void AnimatedBatchSize_AlwaysUsesMultipleFastSteps(int textLength, int expected)
    {
        Assert.Equal(expected, CodexComposerAccessor.AnimatedBatchSize(textLength));
    }

    [Fact]
    public void ProseMirrorGroup_IsRecognizedAsCodexComposer()
    {
        Assert.True(CodexComposerAccessor.IsSupportedComposerShape(
            ControlType.Group,
            "ProseMirror",
            string.Empty));
    }

    [Fact]
    public void RootWebArea_IsNeverRecognizedAsComposer()
    {
        Assert.False(CodexComposerAccessor.IsSupportedComposerShape(
            ControlType.Document,
            string.Empty,
            "RootWebArea"));
    }

    [Fact]
    public void TextBox_RemainsSupportedForNativeFallbacks()
    {
        Assert.True(CodexComposerAccessor.IsSupportedComposerShape(
            ControlType.Edit,
            "TextBox",
            "Composer"));
    }

    [Fact]
    public void AntigravityCursorTextComboBox_IsRecognizedAsComposer()
    {
        Assert.True(CodexComposerAccessor.IsSupportedComposerShape(
            ControlType.ComboBox,
            "max-h-[300px] cursor-text overflow-y-auto text-sm",
            string.Empty));
    }

    [Fact]
    public void UnrelatedComboBox_IsNotRecognizedAsComposer()
    {
        Assert.False(CodexComposerAccessor.IsSupportedComposerShape(
            ControlType.ComboBox,
            "settings-dropdown",
            "ModelSelector"));
    }

    [Fact]
    public void SendInputStructure_MatchesNativeWindowsAbi()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, CodexComposerAccessor.NativeInputSize);
    }
}
