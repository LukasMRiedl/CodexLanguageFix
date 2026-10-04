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
        Assert.True(CodexComposerAccessor.IsComposerSurfaceCandidate(
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

    [Fact]
    public void HermesKeepsLeftmostRightSideControl()
    {
        var voice = new System.Windows.Rect(2280, 1093, 48, 48);
        var send = new System.Windows.Rect(2395, 1093, 56, 57);

        Assert.True(CodexComposerAccessor.IsBetterToolbarAnchor(
            voice,
            send,
            ComposerHost.Hermes));
        Assert.False(CodexComposerAccessor.IsBetterToolbarAnchor(
            send,
            voice,
            ComposerHost.Hermes));
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
    public void SearchRenameAndOtherWritableInputsBlockComposerFallback()
    {
        foreach (var className in new[] { "search-input", "rename-input", "terminal-input", "TextBox" })
        {
            Assert.True(CodexComposerAccessor.IsTextInputForFocusGuard(
                ControlType.Edit, className, "", hasWritableValue: false));
            Assert.False(CodexComposerAccessor.IsSupportedComposerShape(ControlType.Edit, className, ""));
        }

        Assert.True(CodexComposerAccessor.IsTextInputForFocusGuard(
            ControlType.ComboBox, "editable-search", "", hasWritableValue: true));
        Assert.True(CodexComposerAccessor.IsTextInputForFocusGuard(
            ControlType.Custom, "", "", hasWritableValue: true));
    }

    [Fact]
    public void RootWebAreaAndToolbarControlsStillAllowComposerFallback()
    {
        Assert.False(CodexComposerAccessor.IsTextInputForFocusGuard(
            ControlType.Document, "", "RootWebArea", hasWritableValue: false));
        Assert.False(CodexComposerAccessor.IsTextInputForFocusGuard(
            ControlType.Button, "", "", hasWritableValue: false));
        Assert.False(CodexComposerAccessor.IsTextInputForFocusGuard(
            ControlType.ComboBox, "settings-dropdown", "ModelSelector", hasWritableValue: false));
        Assert.True(CodexComposerAccessor.IsTextInputForFocusGuard(
            ControlType.Group, "ProseMirror", "", hasWritableValue: false));
    }

    [Fact]
    public void FocusableDocumentContainerDoesNotBlockToolbarButEditorsAndTextDo()
    {
        Assert.False(CodexComposerAccessor.IsToolbarObstacle(ControlType.Document, "", "RootWebArea"));
        Assert.False(CodexComposerAccessor.IsToolbarObstacle(ControlType.Document, "", ""));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Document, "ProseMirror", ""));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Edit, "ProseMirror", ""));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Text, "", ""));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Button, "", ""));
    }

    [Fact]
    public void OnlyVerifiedContainerAncestorsAreExcludedFromOccupiedBounds()
    {
        foreach (var container in new[] { ControlType.Group, ControlType.Pane, ControlType.Window, ControlType.Custom })
        {
            Assert.False(CodexComposerAccessor.IsToolbarObstacle(container, "", "", isEditorAncestor: true));
            Assert.True(CodexComposerAccessor.IsToolbarObstacle(container, "", "", isEditorAncestor: false));
        }

        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Group, "ProseMirror", "", isEditorAncestor: true));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Text, "", "", isEditorAncestor: true));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Button, "", "", isEditorAncestor: true));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.ComboBox, "", "", isEditorAncestor: true));
        Assert.True(CodexComposerAccessor.IsToolbarObstacle(ControlType.Edit, "", "", isEditorAncestor: true));
    }

    [Fact]
    public void ComposerAutomationIdAloneDoesNotQualifyUnverifiedTextBox()
    {
        Assert.False(CodexComposerAccessor.IsSupportedComposerShape(
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
    public void HermesCursorTextEdit_IsRecognizedAsComposer()
    {
        const string classes = "min-h-[1.625rem] cursor-text overflow-y-auto whitespace-pre-wrap";

        Assert.True(CodexComposerAccessor.IsComposerShapeForHost(
            ControlType.Edit,
            classes,
            ComposerHost.Hermes));
        Assert.False(CodexComposerAccessor.IsComposerShapeForHost(
            ControlType.ComboBox,
            classes,
            ComposerHost.Hermes));
        Assert.True(CodexComposerAccessor.IsComposerShapeForHost(
            ControlType.Edit,
            "ui-prompt-input-editor__input overflow-y-auto",
            ComposerHost.Hermes));
        Assert.False(CodexComposerAccessor.IsComposerShapeForHost(
            ControlType.Edit,
            "not-ui-prompt-input-editor__input overflow-y-auto",
            ComposerHost.Hermes));
        Assert.False(CodexComposerAccessor.IsComposerShapeForHost(
            ControlType.Edit,
            "not-cursor-textual overflow-y-autocomplete",
            ComposerHost.Hermes));
        Assert.True(CodexComposerAccessor.IsSupportedComposerShape(
            ControlType.Edit,
            classes,
            string.Empty));
        Assert.False(CodexComposerAccessor.IsSupportedComposerShape(
            ControlType.Edit,
            "not-cursor-textual overflow-y-autocomplete",
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
