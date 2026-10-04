using System.Windows;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class SidecarPlacementTests
{
    [Fact]
    public void ComposerEqualToEditorUsesOnlyVerifiedAdjacentSpace()
    {
        var editor = new Rect(-900, 100, 300, 100);
        var screen = new Rect(-1200, 0, 900, 800);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], screen, 1, placementBounds: screen));

        Assert.Equal(editor.Right + 4, placement.Left);
        Assert.Equal(editor.Bottom - 28, placement.Top);
        Assert.True(screen.Contains(placement));
        Assert.False(editor.IntersectsWith(placement));
    }

    [Fact]
    public void NoVerifiedPlacementBoundsMeansNoSidecarFallback()
    {
        var editor = new Rect(100, 100, 300, 100);

        Assert.Null(OverlayPlacement.Find(editor, editor, [], new Rect(0, 0, 800, 600), 1));
    }

    [Fact]
    public void AllFourAdjacentSidesBlockedHidesTheSidecar()
    {
        var editor = new Rect(100, 100, 200, 100);
        var scope = new Rect(0, 0, 500, 400);
        Rect[] blockers =
        [
            new(editor.Right + 4, editor.Top, 28, editor.Height),
            new(editor.Left - 32, editor.Top, 28, editor.Height),
            new(editor.Left, editor.Bottom + 4, editor.Width, 28),
            new(editor.Left, editor.Top - 32, editor.Width, 28)
        ];

        Assert.Null(OverlayPlacement.Find(editor, editor, blockers, scope, 1, placementBounds: scope));
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void SidecarUsesScaledDipGapOnNegativeMonitor(double scale)
    {
        var editor = new Rect(-1000 * scale, 100 * scale, 300 * scale, 100 * scale);
        var screen = new Rect(-1200 * scale, 0, 900 * scale, 800 * scale);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], screen, scale, placementBounds: screen));

        Assert.Equal(28 * scale, placement.Width);
        Assert.Equal(editor.Right + 4 * scale, placement.Left);
        Assert.Equal(editor.Bottom - placement.Height, placement.Top);
        Assert.True(screen.Contains(placement));
    }

    [Fact]
    public void SidecarSkipsRightWhenMonitorWorkAreaEndsThere()
    {
        var editor = new Rect(80, 100, 100, 60);
        var screen = new Rect(0, 0, 208, 300);
        var verifiedBounds = new Rect(0, 0, 260, 300);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], screen, 1, placementBounds: verifiedBounds));

        Assert.Equal(editor.Left - 4 - 28, placement.Left);
        Assert.Equal(editor.Bottom - 28, placement.Top);
        Assert.True(screen.Contains(placement));
    }

    [Fact]
    public void WithoutPreferredControlTheToolbarTriesTheEditorFooterRowFirst()
    {
        var composer = new Rect(0, 0, 400, 250);
        var editor = new Rect(40, 40, 300, 100);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            composer, editor, [], new Rect(0, 0, 800, 600), 1));

        Assert.Equal(editor.Bottom + 4, placement.Top);
        Assert.True(composer.Contains(placement));
    }

    [Fact]
    public void BlockedPreferredAnchorFindsNearestFreePositionOnTheSameRow()
    {
        var composer = new Rect(0, 0, 400, 220);
        var editor = new Rect(100, 20, 200, 100);
        var anchor = new Rect(250, 140, 40, 40);
        var blockedAnchor = new Rect(218, 146, 28, 28);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            composer, editor, [anchor, blockedAnchor], new Rect(0, 0, 600, 400), 1, anchor));

        Assert.Equal(186, placement.Left);
        Assert.Equal(anchor.Top + (anchor.Height - placement.Height) / 2, placement.Top);
        Assert.Equal(4, blockedAnchor.Left - placement.Right);
        Assert.False(anchor.IntersectsWith(placement));
    }

    [Fact]
    public void BlockedBottomAlignedRightPositionFindsAnotherFreeSpotOnThatSide()
    {
        var editor = new Rect(100, 100, 200, 100);
        var blockedBottomPosition = new Rect(editor.Right + 4, editor.Bottom - 28, 28, 28);
        var scope = new Rect(0, 0, 500, 400);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [blockedBottomPosition], scope, 1, placementBounds: scope));

        Assert.Equal(editor.Right + 4, placement.Left);
        Assert.Equal(blockedBottomPosition.Top - placement.Height - 4, placement.Top);
        Assert.True(scope.Contains(placement));
    }

    [Fact]
    public void ShortEditorCentersTheSidecarAlongItsAdjacentEdge()
    {
        var editor = new Rect(100, 100, 100, 16);
        var scope = new Rect(0, 0, 500, 400);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], scope, 1, placementBounds: scope));

        Assert.Equal(editor.Right + 4, placement.Left);
        Assert.Equal(editor.Top + (editor.Height - placement.Height) / 2, placement.Top);
        Assert.True(scope.Contains(placement));
    }

    [Fact]
    public void NeighboringObstaclesKeepFourDipClearance()
    {
        var composer = new Rect(0, 0, 500, 300);
        var editor = new Rect(100, 20, 100, 100);
        var anchor = new Rect(300, 150, 40, 40);
        var neighbor = new Rect(238, 156, 27, 28);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            composer, editor, [anchor, neighbor], new Rect(0, 0, 800, 600), 1, anchor));

        Assert.Equal(206, placement.Left);
        Assert.Equal(4, neighbor.Left - placement.Right);
        Assert.Equal(anchor.Top + (anchor.Height - placement.Height) / 2, placement.Top);
    }

    [Fact]
    public void SidecarDoesNotEscapeVerifiedBoundsToFindOpenWindowSpace()
    {
        var editor = new Rect(100, 100, 200, 100);
        var screen = new Rect(0, 0, 800, 600);

        Assert.Null(OverlayPlacement.Find(editor, editor, [], screen, 1,
            placementBounds: editor));
    }

    [Fact]
    public void BlockedFieldAdjacentSpansDoNotPlaceTheButtonFarAcrossTheWindow()
    {
        var editor = new Rect(200, 150, 100, 40);
        var scope = new Rect(0, 0, 800, 600);
        Rect[] blockers =
        [
            new(editor.Right + 4, editor.Top, 28, editor.Height),
            new(editor.Left - 32, editor.Top, 28, editor.Height),
            new(editor.Left, editor.Bottom + 4, editor.Width, 28),
            new(editor.Left, editor.Top - 32, editor.Width, 28)
        ];

        Assert.Null(OverlayPlacement.Find(editor, editor, blockers, scope, 1, placementBounds: scope));
    }

    [Fact]
    public void ExistingToolbarPositionHasPriorityOverEditorSidecar()
    {
        var composer = new Rect(100, 100, 400, 300);
        var editor = new Rect(180, 150, 150, 80);
        var toolbarControl = new Rect(180, 235, 60, 40);
        var screen = new Rect(0, 0, 1000, 800);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            composer, editor, [toolbarControl], screen, 1, toolbarControl, screen));

        Assert.Equal(toolbarControl.Left - 4 - 28, placement.Left);
        Assert.Equal(toolbarControl.Top + (toolbarControl.Height - 28) / 2, placement.Top);
        Assert.False(editor.IntersectsWith(placement));
        Assert.False(toolbarControl.IntersectsWith(placement));
    }

    [Fact]
    public void VerifiedEmptyTextBoundsAllowTopRightPositionInsideComposer()
    {
        var editor = new Rect(100, 100, 200, 100);
        var scope = new Rect(0, 0, 500, 400);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], scope, 1, placementBounds: scope, editorTextBounds: []));

        Assert.Equal(28, placement.Width);
        Assert.Equal(editor.Right - placement.Width - 4, placement.Left);
        Assert.Equal(editor.Top + 4, placement.Top);
        Assert.Equal(4, editor.Right - placement.Right);
        Assert.True(editor.Contains(placement));
        Assert.True(scope.Contains(placement));
    }

    [Fact]
    public void ShortEditorUsesTopRightOfLargerComposerFrame()
    {
        var composer = new Rect(100, 100, 500, 60);
        var editor = new Rect(120, 130, 460, 26);
        var scope = new Rect(0, 0, 800, 600);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            composer, editor, [], scope, 1, placementBounds: scope, editorTextBounds: []));

        Assert.Equal(composer.Right - placement.Width - 4, placement.Left);
        Assert.Equal(composer.Top + 4, placement.Top);
        Assert.True(composer.Contains(placement));
        Assert.False(editor.Contains(placement));
        Assert.True(scope.Contains(placement));
    }

    [Fact]
    public void TopRightEditorPositionMustFitEveryVerifiedBoundsIntersection()
    {
        var editor = new Rect(100, 100, 200, 100);
        var narrowScope = new Rect(100, 100, 160, 100);
        var screen = new Rect(0, 0, 800, 600);

        Assert.Null(OverlayPlacement.Find(editor, editor, [], screen, 1,
            placementBounds: narrowScope, editorTextBounds: []));
    }

    [Fact]
    public void TextInTopRightPositionFallsBackToTheExistingSidecar()
    {
        var editor = new Rect(100, 100, 200, 100);
        var scope = new Rect(0, 0, 500, 400);
        var blockedInnerPosition = new Rect(editor.Right - 32, editor.Top + 4, 28, 28);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], scope, 1, placementBounds: scope,
            editorTextBounds: [blockedInnerPosition]));

        Assert.Equal(editor.Right + 4, placement.Left);
        Assert.Equal(editor.Bottom - placement.Height, placement.Top);
    }

    [Fact]
    public void ActualTextBoundsEqualToEditorStillBlockTopRightComposerPosition()
    {
        var editor = new Rect(100, 100, 200, 100);
        var scope = new Rect(0, 0, 500, 400);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], scope, 1, placementBounds: scope, editorTextBounds: [editor]));

        Assert.Equal(editor.Right + 4, placement.Left);
        Assert.Equal(editor.Bottom - placement.Height, placement.Top);
    }

    [Fact]
    public void OtherControlInTopRightPositionFallsBackToTheExistingSidecar()
    {
        var editor = new Rect(100, 100, 200, 100);
        var scope = new Rect(0, 0, 500, 400);
        var blockingControl = new Rect(editor.Right - 32, editor.Top + 4, 28, 28);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [blockingControl], scope, 1, placementBounds: scope,
            editorTextBounds: []));

        Assert.Equal(editor.Right + 4, placement.Left);
        Assert.Equal(editor.Bottom - placement.Height, placement.Top);
    }

    [Fact]
    public void MissingOrInvalidTextBoundsKeepTheExistingSidecarBehavior()
    {
        var editor = new Rect(100, 100, 200, 100);
        var scope = new Rect(0, 0, 500, 400);

        var missing = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], scope, 1, placementBounds: scope, editorTextBounds: null));
        var invalid = Assert.IsType<Rect>(OverlayPlacement.Find(
            editor, editor, [], scope, 1, placementBounds: scope,
            editorTextBounds: [new Rect(0, 0, 0, 1)]));

        Assert.Equal(editor.Right + 4, missing.Left);
        Assert.Equal(editor.Right + 4, invalid.Left);
    }

    [Fact]
    public void ExistingToolbarPositionPrecedesVerifiedTopRightEditorPosition()
    {
        var composer = new Rect(100, 100, 400, 300);
        var editor = new Rect(180, 150, 150, 80);
        var toolbarControl = new Rect(180, 235, 60, 40);
        var scope = new Rect(0, 0, 1000, 800);

        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(
            composer, editor, [toolbarControl], scope, 1, toolbarControl, scope, []));

        Assert.Equal(toolbarControl.Left - 4 - 28, placement.Left);
        Assert.Equal(toolbarControl.Top + (toolbarControl.Height - 28) / 2, placement.Top);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.MaxValue)]
    public void InvalidScaleIsRejected(double scale)
    {
        var scope = new Rect(0, 0, 500, 400);

        Assert.Null(OverlayPlacement.Find(scope, new Rect(100, 100, 200, 100), [], scope, scale,
            placementBounds: scope));
    }
}
