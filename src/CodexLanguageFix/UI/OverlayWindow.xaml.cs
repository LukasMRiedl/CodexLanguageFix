using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Media;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.UI;

public partial class OverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long ExNoActivate = 0x08000000L;
    private const long ExToolWindow = 0x00000080L;
    private bool? _darkTheme;
    private string? _lastStatusMessage;
    private bool _lastStatusCanUndo;
    private bool _statusVisible;
    private readonly AppLocalizer _localizer;
    private HwndSource? _overlaySource;
    private HwndSource? _statusPopupSource;

    internal int StatusStateChangeCount { get; private set; }

    public OverlayWindow(AppLocalizer? localizer = null)
    {
        _localizer = localizer ?? new AppLocalizer();
        InitializeComponent();
        StatusPopup.PlacementTarget = CorrectButton;
        ApplyLanguage();
        _localizer.LanguageChanged += Localizer_OnLanguageChanged;
        StartBusyAnimation();
        SourceInitialized += (_, _) => ApplyNoActivateStyle();
        StatusPopup.Opened += (_, _) => ApplyPopupNoActivateStyle();
        IsVisibleChanged += (_, _) => StatusPopup.IsOpen = IsVisible && _statusVisible;
    }

    private void Localizer_OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(_localizer.Culture.IetfLanguageTag);
        UndoText.Text = _localizer.Get(AppText.Undo);
        UndoButton.ToolTip = _localizer.Get(AppText.UndoTooltip);
        System.Windows.Automation.AutomationProperties.SetName(UndoButton, _localizer.Get(AppText.Undo));
        CorrectButton.ToolTip = _localizer.Get(AppText.CorrectTooltip);
        System.Windows.Automation.AutomationProperties.SetName(CorrectButton, _localizer.Get(AppText.CorrectAutomationName));
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
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? null : message;
        var hasMessage = normalizedMessage is not null;
        if (string.Equals(_lastStatusMessage, normalizedMessage, StringComparison.Ordinal)
            && _lastStatusCanUndo == canUndo)
        {
            return;
        }

        _lastStatusMessage = normalizedMessage;
        _lastStatusCanUndo = canUndo;
        _statusVisible = hasMessage;
        StatusStateChangeCount++;

        StatusText.Text = normalizedMessage ?? string.Empty;
        StatusText.ToolTip = normalizedMessage;
        SuccessGlyph.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Visibility = canUndo ? Visibility.Collapsed : Visibility.Visible;
        UndoSeparator.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        StatusPill.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        StatusPopup.IsOpen = IsVisible && hasMessage;
    }

    public void PositionAt(
        Rect composerBounds,
        Rect? rightControlBounds = null,
        ComposerHost host = ComposerHost.Codex,
        Rect? editorBounds = null,
        IReadOnlyList<Rect>? occupiedBounds = null)
    {
        if (composerBounds.IsEmpty)
        {
            Hide();
            return;
        }
        var center = new NativePoint
        {
            X = (int)Math.Round(composerBounds.Left + composerBounds.Width / 2),
            Y = (int)Math.Round(composerBounds.Top + composerBounds.Height / 2)
        };
        var monitor = MonitorFromPoint(center, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            Hide();
            return;
        }
        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96d : 1d;
        var screen = new Rect(info.Work.Left, info.Work.Top,
            info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        var obstacles = (occupiedBounds ?? Array.Empty<Rect>()).ToList();
        if (rightControlBounds is { IsEmpty: false } control)
            obstacles.Add(control);
        // Without a separately identified editor the entire composer is protected.
        var placement = OverlayPlacement.Find(composerBounds, editorBounds ?? composerBounds,
            obstacles, screen, scale, rightControlBounds);
        if (placement is not { } bounds)
        {
            Hide();
            return;
        }
        ApplyAdaptiveTheme(composerBounds);
        var handle = new WindowInteropHelper(this).EnsureHandle();
        // Screen coordinates stay physical; WPF DIP conversion cannot use another monitor's origin.
        if (!SetWindowPos(handle, new nint(-1), (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0010))
        {
            Hide();
            return;
        }
        if (!IsVisible)
            Show();
        if (!SetWindowPos(handle, new nint(-1), (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0010))
        {
            Hide();
            return;
        }
        if (StatusPopup.IsOpen)
        {
            StatusPopup.HorizontalOffset += 1;
            StatusPopup.HorizontalOffset -= 1;
        }
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

    protected override void OnClosed(EventArgs e)
    {
        StatusPopup.IsOpen = false;
        if (_overlaySource is { IsDisposed: false }) _overlaySource.RemoveHook(NoActivateHook);
        if (_statusPopupSource is { IsDisposed: false }) _statusPopupSource.RemoveHook(NoActivateHook);
        _overlaySource = null;
        _statusPopupSource = null;
        _localizer.LanguageChanged -= Localizer_OnLanguageChanged;
        base.OnClosed(e);
    }

    private void CorrectButton_OnClick(object sender, RoutedEventArgs e) => CorrectRequested?.Invoke(this, EventArgs.Empty);
    private void UndoButton_OnClick(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyNoActivateStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _overlaySource = HwndSource.FromHwnd(handle);
        _overlaySource?.AddHook(NoActivateHook);
        ApplyNoActivateStyle(handle);
    }

    private void ApplyPopupNoActivateStyle()
    {
        if (PresentationSource.FromVisual(StatusPill) is not HwndSource source) return;
        if (!ReferenceEquals(_statusPopupSource, source))
        {
            if (_statusPopupSource is { IsDisposed: false }) _statusPopupSource.RemoveHook(NoActivateHook);
            _statusPopupSource = source;
            source.AddHook(NoActivateHook);
        }
        ApplyNoActivateStyle(source.Handle);
    }

    private static void ApplyNoActivateStyle(nint handle)
    {
        var current = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyleIndex, new nint(current | ExNoActivate | ExToolWindow));
    }

    internal static nint NoActivateHook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0021) return nint.Zero; // WM_MOUSEACTIVATE
        handled = true;
        return new nint(3); // MA_NOACTIVATE: Klick verarbeiten, Fenster nicht aktivieren.
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);
}
