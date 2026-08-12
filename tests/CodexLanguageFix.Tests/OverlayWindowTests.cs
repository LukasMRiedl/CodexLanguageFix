using CodexLanguageFix.UI;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class OverlayWindowTests
{
    [Theory]
    [InlineData(1, "1 Änderung")]
    [InlineData(2, "2 Änderungen")]
    public void ChangeCount_UsesCorrectGermanGrammar(int count, string expected)
    {
        Assert.Equal(expected, CorrectionCoordinator.FormatChangeCount(count));
    }

    [Theory]
    [InlineData("Das ist korrekt.", "Das ist korrekt.", true)]
    [InlineData("Das ist korrekt.\r", "Das ist korrekt.", true)]
    [InlineData("Das ist korrekt.\n", "Das ist korrekt.", true)]
    [InlineData("Anderer Text", "Das ist korrekt.", false)]
    public void ProviderText_AllowsOnlyOneTerminalAutomationMarker(
        string observed,
        string expected,
        bool matches)
    {
        Assert.Equal(matches, CorrectionCoordinator.MatchesProviderText(observed, expected));
    }

    [Fact]
    public void StatusLayout_ExpandsAndReturnsToCompactWidth()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var overlay = new OverlayWindow();
                overlay.ShowStatus(null);
                Assert.Equal(36, overlay.Width);
                Assert.Equal(36, overlay.Height);

                overlay.ShowStatus("2 Änderungen", canUndo: true);
                Assert.InRange(overlay.Width, 144, 326);
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
                Assert.Equal(System.Windows.Visibility.Visible, overlay.BusyGlyph.Visibility);
                Assert.True(overlay.BusyRotation.HasAnimatedProperties);
                overlay.SetBusy(false);
                Assert.Equal(System.Windows.Visibility.Collapsed, overlay.BusyGlyph.Visibility);
                Assert.True(overlay.BusyRotation.HasAnimatedProperties);
                overlay.ShowStatus(null);
                Assert.Equal(36, overlay.Width);
                Assert.Equal(36, overlay.Height);
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
    [InlineData(100, 1200, 36, 314)]
    [InlineData(100, 784, 36, 303.84)]
    [InlineData(100, 600, 36, 256)]
    [InlineData(100, 120, 36, 131.2)]
    [InlineData(100, 60, 36, 116)]
    [InlineData(100, 40, 36, 102)]
    public void FallbackPosition_RemainsInsideComposerAtEverySupportedWidth(
        double composerLeft,
        double composerWidth,
        double overlayWidth,
        double expected)
    {
        var left = OverlayWindow.CalculateFallbackLeft(composerLeft, composerWidth, overlayWidth);

        Assert.Equal(expected, left, precision: 2);
        Assert.True(left >= composerLeft);
        Assert.True(left + overlayWidth <= composerLeft + composerWidth);
    }

    [Theory]
    [InlineData(1100, 36, 1059)]
    [InlineData(1100, 180, 915)]
    [InlineData(980, 180, 795)]
    public void AnchoredPosition_FollowsVariableRightControlWidth(
        double rightControlLeft,
        double overlayWidth,
        double expected)
    {
        Assert.Equal(expected, OverlayWindow.CalculateAnchoredLeft(rightControlLeft, overlayWidth));
    }

    [Fact]
    public void HostSpecificGap_MatchesEachNativeToolbar()
    {
        Assert.Equal(5, OverlayWindow.GetRightControlGap(CodexLanguageFix.Contracts.ComposerHost.Codex));
        Assert.Equal(1, OverlayWindow.GetRightControlGap(CodexLanguageFix.Contracts.ComposerHost.Antigravity));
    }

    [Theory]
    [InlineData(546.5, 28.5, 36, 542.75)]
    [InlineData(420, 40, 36, 422)]
    public void VerticalPosition_UsesNativeControlCenter(
        double controlTop,
        double controlHeight,
        double overlayHeight,
        double expected)
    {
        Assert.Equal(
            expected,
            OverlayWindow.CalculateVerticalTop(
                controlTop,
                controlHeight,
                overlayHeight),
            precision: 2);
    }

    [Theory]
    [InlineData(560, 510, 24, true)]
    [InlineData(560, 420, 120, true)]
    [InlineData(560, 356, 120, true)]
    [InlineData(560, 355, 120, false)]
    [InlineData(560, 510, 23, false)]
    [InlineData(300, 510, 120, false)]
    [InlineData(700, 510, 120, false)]
    public void RightControlValidation_SupportsVariableWidthsWithoutAcceptingUnrelatedControls(
        double left,
        double top,
        double width,
        bool expected)
    {
        var composer = new System.Windows.Rect(100, 400, 600, 100);
        var control = new System.Windows.Rect(left, top, width, 32);
        Assert.Equal(expected, OverlayWindow.IsUsableRightControl(control, composer));
    }
}
