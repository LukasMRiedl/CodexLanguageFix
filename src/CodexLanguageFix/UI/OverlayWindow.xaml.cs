using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.UI;

public partial class OverlayWindow : Window
{
    private const double CodexRightControlGap = 5;
    private const double AntigravityRightControlGap = 1;
    private const int ExtendedStyleIndex = -20;
    private const long ExNoActivate = 0x08000000L;
    private const long ExToolWindow = 0x00000080L;
    private Rect _lastComposerBounds = Rect.Empty;
    private Rect? _lastRightControlBounds;
    private ComposerHost _lastHost = ComposerHost.Codex;
    private bool? _darkTheme;
    private string? _lastStatusMessage;
    private bool _lastStatusCanUndo;
    private bool _statusVisible;

    internal int StatusStateChangeCount { get; private set; }

    public OverlayWindow()
    {
        InitializeComponent();
        StartBusyAnimation();
        SourceInitialized += (_, _) => ApplyNoActivateStyle();
    }

    public event EventHandler? CorrectRequested;
    public event EventHandler? UndoRequested;

    public void SetBusy(bool busy)
    {
        CorrectButton.IsEnabled = !busy;
        UndoButton.IsEnabled = !busy;
        GrammarGlyph.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        BusyGlyph.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartBusyAnimation()
    {
        BusyRotation.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(520))
            {
                RepeatBehavior = RepeatBehavior.Forever
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    public void ShowStatus(string? message, bool canUndo = false)
    {
        const double compactSize = 36;
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? null : message;
        var hasMessage = normalizedMessage is not null;
        if (string.Equals(_lastStatusMessage, normalizedMessage, StringComparison.Ordinal)
            && _lastStatusCanUndo == canUndo)
        {
            return;
        }

        var wasVisible = _statusVisible;
        _lastStatusMessage = normalizedMessage;
        _lastStatusCanUndo = canUndo;
        _statusVisible = hasMessage;
        StatusStateChangeCount++;

        StatusText.Text = normalizedMessage ?? string.Empty;
        SuccessGlyph.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Visibility = canUndo ? Visibility.Collapsed : Visibility.Visible;
        UndoSeparator.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        StatusPill.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        StatusGap.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        if (hasMessage)
        {
            StatusPill.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Width = Math.Clamp(StatusPill.DesiredSize.Width + 4 + compactSize, 144, 326);
            Height = compactSize;
        }
        else
        {
            Width = compactSize;
            Height = compactSize;
        }
        if (hasMessage && !wasVisible)
        {
            StatusPill.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            StatusTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else if (hasMessage)
        {
            StatusPill.BeginAnimation(OpacityProperty, null);
            StatusTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            StatusPill.Opacity = 1;
            StatusTranslate.X = 0;
        }
        else
        {
            StatusPill.BeginAnimation(OpacityProperty, null);
            StatusTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            StatusPill.Opacity = 0;
            StatusTranslate.X = 4;
        }

        RepositionAfterLayout();
    }

    public void PositionAt(
        Rect composerBounds,
        Rect? rightControlBounds = null,
        ComposerHost host = ComposerHost.Codex)
    {
        _lastComposerBounds = composerBounds;
        _lastHost = host;
        if (IsUsableRightControl(rightControlBounds, composerBounds))
        {
            _lastRightControlBounds = rightControlBounds;
        }
        else if (!IsUsableRightControl(_lastRightControlBounds, composerBounds))
        {
            _lastRightControlBounds = null;
        }
        ApplyAdaptiveTheme(composerBounds);
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();

        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new System.Windows.Point(composerBounds.Left, composerBounds.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(composerBounds.Right, composerBounds.Bottom));
        var availableWidth = Math.Max(0, bottomRight.X - topLeft.X);
        const double toolbarGap = 4;
        double? anchoredTop = null;

        // Der reale Modell-/Mikrofonknopf ist der stabile Anker. Aktuelle Codex-
        // Layouts legen ihn entweder in dieselbe Zeile wie den Text oder unter
        // einen mehrzeiligen Textbereich.
        if (_lastRightControlBounds is { } controlBounds)
        {
            var controlTopLeft = transform.Transform(new System.Windows.Point(controlBounds.Left, controlBounds.Top));
            var controlBottomRight = transform.Transform(new System.Windows.Point(controlBounds.Right, controlBounds.Bottom));
            Left = CalculateAnchoredLeft(controlTopLeft.X, Width, GetRightControlGap(host));
            anchoredTop = CalculateVerticalTop(
                controlTopLeft.Y,
                controlBottomRight.Y - controlTopLeft.Y,
                Height);
        }
        else
        {
            Left = CalculateFallbackLeft(topLeft.X, availableWidth, Width);
        }
        Top = anchoredTop ?? bottomRight.Y + toolbarGap;
    }

    internal static double CalculateAnchoredLeft(double rightControlLeft, double overlayWidth, double gap = CodexRightControlGap) =>
        rightControlLeft - gap - overlayWidth;

    internal static double GetRightControlGap(ComposerHost host) =>
        host == ComposerHost.Antigravity
            ? AntigravityRightControlGap
            : CodexRightControlGap;

    internal static double CalculateVerticalTop(
        double controlTop,
        double controlHeight,
        double overlayHeight) =>
        controlTop + (controlHeight - overlayHeight) / 2;

    internal static bool IsUsableRightControl(Rect? rightControlBounds, Rect composerBounds)
    {
        if (rightControlBounds is not { } bounds || bounds.IsEmpty || composerBounds.IsEmpty)
        {
            return false;
        }

        var maximumTopDistance = Math.Max(48, composerBounds.Height);
        return bounds.Width >= 24
            && bounds.Left >= composerBounds.Left + composerBounds.Width * 0.35
            && bounds.Left < composerBounds.Right
            && bounds.Bottom >= composerBounds.Top - 12
            && bounds.Top <= composerBounds.Bottom + maximumTopDistance;
    }

    internal static double CalculateFallbackLeft(
        double composerLeft,
        double availableWidth,
        double overlayWidth)
    {
        const double horizontalInset = 8;
        var preferredOffset = Math.Clamp(availableWidth * 0.26, 24, 214);
        var usableInset = Math.Min(
            horizontalInset,
            Math.Max(0, (availableWidth - overlayWidth) / 2));
        var maximumOffset = Math.Max(
            usableInset,
            availableWidth - overlayWidth - usableInset);
        return composerLeft + Math.Min(preferredOffset, maximumOffset);
    }

    private void RepositionAfterLayout()
    {
        if (_lastComposerBounds.IsEmpty)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => PositionAt(_lastComposerBounds, _lastRightControlBounds, _lastHost));
    }

    private void ApplyAdaptiveTheme(Rect composerBounds)
    {
        var dark = DetectDarkSurface(composerBounds) ?? true;
        if (_darkTheme == dark)
        {
            return;
        }

        _darkTheme = dark;
        if (dark)
        {
            SetBrush("OverlaySurfaceBrush", "#F02B2B2D");
            SetBrush("OverlayBorderBrush", "#26FFFFFF");
            SetBrush("ButtonBrush", "#00FFFFFF");
            SetBrush("ButtonHoverBrush", "#12FFFFFF");
            SetBrush("ButtonPressedBrush", "#1EFFFFFF");
            SetBrush("PrimaryTextBrush", "#E9E9EE");
            SetBrush("SecondaryTextBrush", "#B8B9BE");
            SetBrush("AccentBrush", "#78BFA9");
            SetBrush("SeparatorBrush", "#26FFFFFF");
        }
        else
        {
            SetBrush("OverlaySurfaceBrush", "#F8FFFFFF");
            SetBrush("OverlayBorderBrush", "#18000000");
            SetBrush("ButtonBrush", "#00000000");
            SetBrush("ButtonHoverBrush", "#0C000000");
            SetBrush("ButtonPressedBrush", "#16000000");
            SetBrush("PrimaryTextBrush", "#303136");
            SetBrush("SecondaryTextBrush", "#6B6C72");
            SetBrush("AccentBrush", "#18785F");
            SetBrush("SeparatorBrush", "#1F000000");
        }
    }

    private void SetBrush(string key, string color) =>
        Resources[key] = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private static bool? DetectDarkSurface(Rect composerBounds)
    {
        var deviceContext = GetDC(nint.Zero);
        if (deviceContext == nint.Zero)
        {
            return null;
        }

        try
        {
            var y = (int)Math.Round(composerBounds.Bottom - 14);
            var sampleXs = new[]
            {
                (int)Math.Round(composerBounds.Left + 20),
                (int)Math.Round(composerBounds.Left + composerBounds.Width * 0.5),
                (int)Math.Round(composerBounds.Right - 90)
            };
            var luminance = 0d;
            var count = 0;
            foreach (var x in sampleXs)
            {
                var pixel = GetPixel(deviceContext, x, y);
                if (pixel == uint.MaxValue)
                {
                    continue;
                }

                var red = pixel & 0xFF;
                var green = (pixel >> 8) & 0xFF;
                var blue = (pixel >> 16) & 0xFF;
                luminance += (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255d;
                count++;
            }

            return count == 0 ? null : luminance / count < 0.52;
        }
        finally
        {
            _ = ReleaseDC(nint.Zero, deviceContext);
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        // Das Fenster darf den Eingabefokus von Codex nicht übernehmen.
    }

    private void CorrectButton_OnClick(object sender, RoutedEventArgs e) => CorrectRequested?.Invoke(this, EventArgs.Empty);
    private void UndoButton_OnClick(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyNoActivateStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var current = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyleIndex, new nint(current | ExNoActivate | ExToolWindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);
}
