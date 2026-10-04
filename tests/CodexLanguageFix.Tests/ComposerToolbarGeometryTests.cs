using System.Windows;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class ComposerToolbarGeometryTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void AssociatesOnlyImmediateToolbarAndPreservesEveryOccupiedControl(double scale)
    {
        var editor = Scale(new Rect(100, 100, 600, 80), scale);
        var document = Scale(new Rect(0, 0, 1000, 1000), scale);
        var label = Scale(new Rect(240, 190, 150, 24), scale);
        var controls = new[]
        {
            new ComposerControlBounds(editor, false, true),
            Button(Scale(new Rect(100, 186, 32, 32), scale)),
            Button(Scale(new Rect(654, 186, 32, 32), scale)),
            new ComposerControlBounds(label, false, false),
            Button(Scale(new Rect(20, 190, 32, 32), scale)),
            Button(Scale(new Rect(100, 400, 32, 32), scale))
        };

        var result = Assert.IsType<ComposerToolbarAssociation>(ComposerToolbarGeometry.TryAssociate(editor, document, controls, scale));
        Assert.True(result.SurfaceBounds.Contains(editor));
        Assert.Equal(2, result.RowButtons.Count);
        Assert.Contains(label, result.OccupiedBounds);
        Assert.Contains(editor, result.OccupiedBounds);
        Assert.True(result.SurfaceBounds.Bottom < 230 * scale);
    }

    [Fact]
    public void RejectsSingleButtonOrButtonsOnlyOnRightSide()
    {
        var editor = new Rect(100, 100, 600, 80);
        var document = new Rect(0, 0, 1000, 1000);
        var right = Button(new Rect(650, 186, 32, 32));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document, [right], 1));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document, [right, Button(new Rect(600, 186, 32, 32))], 1));
    }

    [Fact]
    public void RejectsSeparateRowsAndUnrelatedDistantToolbar()
    {
        var editor = new Rect(100, 100, 600, 80);
        var document = new Rect(0, 0, 1000, 1000);
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document,
            [Button(new Rect(100, 184, 32, 32)), Button(new Rect(650, 202, 32, 32))], 1));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document,
            [Button(new Rect(100, 250, 32, 32)), Button(new Rect(650, 250, 32, 32))], 1));
    }

    [Fact]
    public void RejectsMultiplePossibleToolbarRows()
    {
        var editor = new Rect(100, 100, 600, 80);
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, new Rect(0, 0, 1000, 1000),
            [Button(new Rect(100, 181, 18, 18)), Button(new Rect(650, 181, 18, 18)),
             Button(new Rect(100, 202, 18, 18)), Button(new Rect(650, 202, 18, 18))], 1));
    }

    [Fact]
    public void RejectsSurfaceIntersectingAnotherEditor()
    {
        var editor = new Rect(100, 100, 600, 80);
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, new Rect(0, 0, 1000, 1000),
            [Button(new Rect(100, 186, 32, 32)), Button(new Rect(650, 186, 32, 32)),
             new ComposerControlBounds(new Rect(300, 190, 250, 24), false, true)], 1));
    }

    [Fact]
    public void SupportsNegativeMonitorCoordinatesAndClipsMarginToDocument()
    {
        var editor = new Rect(-800, 100, 600, 80);
        var document = new Rect(-800, 0, 800, 220);
        var result = Assert.IsType<ComposerToolbarAssociation>(ComposerToolbarGeometry.TryAssociate(editor, document,
            [Button(new Rect(-800, 186, 32, 32)), Button(new Rect(-240, 186, 32, 32))], 1));
        Assert.True(document.Contains(result.SurfaceBounds));
        Assert.Equal(-800, result.SurfaceBounds.Left);
    }

    [Fact]
    public void ObservedWorkSurfaceHasRoomForTwentyEightDipButtonBelowEditor()
    {
        var surface = new Rect(833, 1400, 728, 232);
        var editor = new Rect(849, 1408, 696, 152);
        var document = new Rect(240, 32, 2400, 1640);

        Rect[] occupied = [editor, new(849, 1409, 211, 37), new(897, 1521, 319, 37),
            new(841, 1568, 56, 56), new(907, 1568, 60, 56), new(1136, 1568, 289, 56),
            new(1424, 1568, 57, 56), new(1496, 1568, 57, 56)];
        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(surface, editor, occupied, document, 2,
            new Rect(1136, 1568, 289, 56)));

        Assert.Equal(56, placement.Width);
        Assert.True(surface.Contains(placement));
        Assert.False(editor.IntersectsWith(placement));
        Assert.True(placement.Top > editor.Bottom);
        Assert.DoesNotContain(occupied, bounds => bounds.IntersectsWith(placement));
    }

    [Fact]
    public void ObservedInlineChatToolbarPlacesButtonImmediatelyLeftOfAttachment()
    {
        var editor = new Rect(1012, 1604, 319, 40);
        var attachment = new Rect(946, 1596, 56, 56);
        var model = new Rect(1340, 1596, 132, 56);
        var document = new Rect(800, 1200, 1000, 600);
        var association = Assert.IsType<ComposerToolbarAssociation>(ComposerToolbarGeometry.TryAssociate(editor, document,
            [new(editor, false, true), Button(attachment), Button(model), Button(new Rect(1472, 1596, 56, 56))], 2));
        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(association.SurfaceBounds, editor,
            association.OccupiedBounds, document, 2, model));

        Assert.Equal(56, placement.Width);
        Assert.Equal(attachment.Top, placement.Top);
        Assert.Equal(attachment.Left - 8, placement.Right);
        Assert.DoesNotContain(association.OccupiedBounds, occupied => occupied.IntersectsWith(placement));
    }

    [Fact]
    public void ForeignTextInInlineLeftMarginLeavesNoSafePlacement()
    {
        var editor = new Rect(1012, 1604, 319, 40);
        var model = new Rect(1340, 1596, 132, 56);
        var document = new Rect(800, 1200, 1000, 600);
        var association = Assert.IsType<ComposerToolbarAssociation>(ComposerToolbarGeometry.TryAssociate(editor, document,
            [new(editor, false, true), Button(new Rect(946, 1596, 56, 56)), Button(model),
             new(new Rect(874, 1588, 72, 72), false, false)], 2));
        Assert.Null(OverlayPlacement.Find(association.SurfaceBounds, editor, association.OccupiedBounds, document, 2, model));
    }

    [Fact]
    public void InlineAssociationRequiresUnambiguousCloseButtonsOnBothSides()
    {
        var editor = new Rect(1012, 1604, 319, 40);
        var document = new Rect(800, 1200, 1000, 600);
        var left = Button(new Rect(946, 1596, 56, 56));
        var right = Button(new Rect(1340, 1596, 132, 56));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document, [left], 2));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document, [right], 2));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document, [left, Button(new Rect(1400, 1596, 132, 56))], 2));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document, [left, right, Button(new Rect(1341, 1596, 132, 56))], 2));
        Assert.Null(ComposerToolbarGeometry.TryAssociate(editor, document,
            [left, right, new(new Rect(875, 1596, 64, 56), false, true)], 2));
    }

    [Fact]
    public void VisibleTextLinesPreserveRealObstaclesWithoutBlockingWhitespaceInsideAggregateBox()
    {
        var surface = new Rect(842, 1504, 778, 186);
        var editor = new Rect(858, 1512, 746, 88);
        var document = new Rect(240, 32, 2400, 1658);
        Rect[] buttons = [new(850, 1608, 56, 56), new(916, 1608, 60, 56), new(1160, 1608, 324, 56),
            new(1484, 1608, 56, 56), new(1556, 1608, 56, 56)];
        Rect[] aggregate = [new(864, 1299, 732, 311), new(864, 1646, 734, 40)];
        Assert.Null(OverlayPlacement.Find(surface, editor, buttons.Concat(aggregate).ToArray(), document, 2, buttons[2]));

        // Synthetische sichtbare Zeilen belegen die Wirkung der präziseren UIA-Geometrie.
        Rect[] visibleLines = [new(864, 1299, 732, 200), new(864, 1672, 734, 18)];
        var placement = Assert.IsType<Rect>(OverlayPlacement.Find(surface, editor, buttons.Concat(visibleLines).ToArray(), document, 2, buttons[2]));
        Assert.DoesNotContain(visibleLines, line => line.IntersectsWith(placement));
        Assert.InRange(placement.Left, 976, 1104);
        Assert.Null(OverlayPlacement.Find(surface, editor,
            buttons.Concat(visibleLines).Append(new Rect(976, 1608, 184, 56)).ToArray(), document, 2, buttons[2]));
    }

    [Fact]
    public void NarrowFloatingWindowNeverPlacesInlineButtonOutsideItsOwnScope()
    {
        var editor = new Rect(1012, 1604, 319, 40);
        var floatingWindow = new Rect(918, 400, 710, 1280);
        var model = new Rect(1340, 1596, 132, 56);
        var association = Assert.IsType<ComposerToolbarAssociation>(ComposerToolbarGeometry.TryAssociate(editor, floatingWindow,
            [new(editor, false, true), Button(new Rect(946, 1596, 56, 56)), Button(model)], 2));

        Assert.True(floatingWindow.Contains(association.SurfaceBounds));
        Assert.Equal(floatingWindow.Left, association.SurfaceBounds.Left);
        Assert.Null(OverlayPlacement.Find(association.SurfaceBounds, editor, association.OccupiedBounds, floatingWindow, 2, model));
    }

    private static ComposerControlBounds Button(Rect bounds) => new(bounds, true, false);
    private static Rect Scale(Rect value, double scale) => new(value.X * scale, value.Y * scale, value.Width * scale, value.Height * scale);
}
