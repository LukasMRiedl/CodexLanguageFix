using CodexLanguageFix.UI;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class OverlayWindowTests
{
    [Theory]
    [InlineData("de", 1, "1 Änderung")]
    [InlineData("de", 2, "2 Änderungen")]
    [InlineData("en", 1, "1 change")]
    [InlineData("en", 2, "2 changes")]
    public void ChangeCount_UsesSelectedLanguage(string language, int count, string expected)
    {
        Assert.Equal(expected, CorrectionCoordinator.FormatChangeCount(count, new AppLocalizer(language)));
    }

    [Theory]
    [InlineData("de", 1, "1 Änderung · Luna")]
    [InlineData("en", 2, "2 changes · Luna")]
    public void LunaChangeCount_IncludesProvider(string language, int count, string expected)
    {
        Assert.Equal(expected, CorrectionCoordinator.FormatChangeCount(count, CorrectionProviderKind.Luna, new AppLocalizer(language)));
    }

    [Fact]
    public void StatusLayout_RemainsCompactAndUsesSeparatePopup()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var overlay = new OverlayWindow(new AppLocalizer("de"));
                var correctionRequests = 0;
                var undoRequests = 0;
                overlay.CorrectRequested += (_, _) => correctionRequests++;
                overlay.UndoRequested += (_, _) => undoRequests++;
                overlay.ShowStatus(null);
                Assert.Equal(36, overlay.Width);
                Assert.Equal(36, overlay.Height);
                Assert.False(overlay.StatusPopup.IsOpen);
                Assert.False(overlay.StatusPopup.Focusable);
                Assert.False(overlay.CorrectButton.Focusable);
                Assert.False(overlay.UndoButton.Focusable);

                overlay.ShowStatus("2 Änderungen", canUndo: true);
                Assert.Equal(36, overlay.Width);
                Assert.Same(overlay.CorrectButton, overlay.StatusPopup.PlacementTarget);
                Assert.Equal(36, overlay.Height);
                Assert.Equal(15, overlay.StatusPill.Height);
                Assert.Equal(13, overlay.UndoButton.Height);

                var stableWidth = overlay.Width;
                var stateChanges = overlay.StatusStateChangeCount;
                for (var index = 0; index < 5; index++)
                {
                    overlay.ShowStatus("2 Änderungen", canUndo: true);
                }

                Assert.Equal(stableWidth, overlay.Width);
                Assert.Equal(stateChanges, overlay.StatusStateChangeCount);
                overlay.SetBusy(true);
                Assert.Equal("2 Änderungen", overlay.StatusText.Text);
                Assert.Equal(36, overlay.Width);

                overlay.SetBusy(true);
                Assert.Equal(System.Windows.Visibility.Visible, overlay.BusyGlyph.Visibility);
                Assert.True(overlay.BusyRotation.HasAnimatedProperties);
                overlay.SetBusy(false);
                Assert.Equal(System.Windows.Visibility.Collapsed, overlay.BusyGlyph.Visibility);
                Assert.True(overlay.BusyRotation.HasAnimatedProperties);
                overlay.ShowStatus(null);
                Assert.Equal(36, overlay.Width);
                Assert.Equal(36, overlay.Height);
                Assert.Equal(0, correctionRequests);
                Assert.Equal(0, undoRequests);
                overlay.UndoButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(0, correctionRequests);
                Assert.Equal(1, undoRequests);
                overlay.CorrectButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(1, correctionRequests);
                Assert.Equal(1, undoRequests);
                overlay.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Der WPF-Layouttest hat das Zeitlimit überschritten.");
        if (failure is not null)
        {
            throw failure;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Placement_UsesPhysicalPixelsAndAvoidsEditorAndControls(double scale)
    {
        var composer = new System.Windows.Rect(-1200 * scale, 100 * scale, 600 * scale, 200 * scale);
        var editor = new System.Windows.Rect(-1190 * scale, 110 * scale, 580 * scale, 130 * scale);
        var control = new System.Windows.Rect(-650 * scale, 250 * scale, 40 * scale, 40 * scale);
        var screen = new System.Windows.Rect(-1600 * scale, 0, 1600 * scale, 1000 * scale);
        var bounds = OverlayPlacement.Find(composer, editor, [control], screen, scale, control);
        Assert.NotNull(bounds);
        Assert.Equal(36 * scale, bounds.Value.Width);
        Assert.True(composer.Contains(bounds.Value));
        Assert.True(screen.Contains(bounds.Value));
        Assert.False(editor.IntersectsWith(bounds.Value));
        Assert.False(control.IntersectsWith(bounds.Value));
        Assert.True(bounds.Value.Left < 0);
    }

    [Fact]
    public void Placement_FindsFreeGapBetweenToolbarControls()
    {
        var composer = new System.Windows.Rect(100, 100, 600, 200);
        var editor = new System.Windows.Rect(100, 100, 600, 150);
        System.Windows.Rect[] controls = [new(100, 250, 400, 50), new(550, 250, 150, 50)];
        var placement = OverlayPlacement.Find(composer, editor, controls,
            new System.Windows.Rect(0, 0, 1920, 1080), 1, controls[1]);
        Assert.NotNull(placement);
        Assert.InRange(placement.Value.Left, 500, 514);
        Assert.DoesNotContain(controls, r => r.IntersectsWith(placement.Value));
    }

    [Fact]
    public void Placement_HidesWhenEditorOrScreenLeavesNoSpace()
    {
        var composer = new System.Windows.Rect(0, 0, 600, 200);
        Assert.Null(OverlayPlacement.Find(composer, composer, [], composer, 1));
        Assert.Null(OverlayPlacement.Find(composer, new System.Windows.Rect(0, 0, 600, 150),
            [], new System.Windows.Rect(0, 0, 600, 160), 1));
        Assert.Null(OverlayPlacement.Find(composer, System.Windows.Rect.Empty, [], composer, 1));
    }
}
