using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

using System.Windows.Markup;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.UI;

public partial class SettingsWindow : Window
{
    private readonly AppLocalizer _localizer;
    private readonly Func<CorrectionShortcut, string?> _saveShortcut;
    private CorrectionShortcut _shortcut;
    private string _status = string.Empty;
    private string? _inlineMessage;
    private bool _inlineMessageIsError;
    private bool _isCapturing;

    public SettingsWindow(
        AppLocalizer localizer,
        CorrectionShortcut shortcut,
        string status,
        Func<CorrectionShortcut, string?> saveShortcut)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _shortcut = shortcut;
        _saveShortcut = saveShortcut ?? throw new ArgumentNullException(nameof(saveShortcut));

        InitializeComponent();
        ApplyLanguage();
        UpdateStatus(status);
        UpdateShortcutDisplay();

        _localizer.LanguageChanged += Localizer_OnLanguageChanged;
        Closed += (_, _) => _localizer.LanguageChanged -= Localizer_OnLanguageChanged;
    }

    public void UpdateStatus(string status)
    {
        _status = status ?? string.Empty;
        StatusTextBox.Text = string.IsNullOrWhiteSpace(_status)
            ? _localizer.Get(AppText.NoStatus)
            : _status;
    }

    private void Localizer_OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        Language = XmlLanguage.GetLanguage(_localizer.Culture.IetfLanguageTag);
        Title = _localizer.Get(AppText.Settings);
        HeadingText.Text = _localizer.Get(AppText.Settings);
        RecordButton.Content = _localizer.Get(AppText.RecordShortcut);
        RestoreButton.Content = _localizer.Get(AppText.RestoreDefaultShortcut);
        SaveButton.Content = _localizer.Get(AppText.Save);
        CancelButton.Content = _localizer.Get(AppText.Cancel);
        ShortcutHintText.Text = _localizer.Get(AppText.ShortcutHint);
        LastStatusLabel.Text = _localizer.Get(AppText.LastStatus);
        System.Windows.Automation.AutomationProperties.SetName(ShortcutDisplayTextBox, _localizer.Get(AppText.RecordShortcut));
        System.Windows.Automation.AutomationProperties.SetName(StatusTextBox, _localizer.Get(AppText.LastStatus));
        UpdateStatus(_status);
        UpdateShortcutDisplay();
        ShowInlineMessage(_inlineMessage, _inlineMessageIsError);
    }

    private void UpdateShortcutDisplay()
    {
        ShortcutDisplayTextBox.Text = _shortcut.GetDisplayText(_localizer);
    }

    private void RecordButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            CancelCapture();
            return;
        }

        _isCapturing = true;
        _inlineMessage = null;
        SetInlineMessage(_localizer.Get(AppText.RecordShortcutPrompt), isError: false);
        SetCaptureControlsEnabled(false);
        Keyboard.Focus(this);
    }

    private void RestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        _shortcut = CorrectionShortcut.Default;
        UpdateShortcutDisplay();
        SetInlineMessage(_localizer.Get(AppText.ShortcutReset), isError: false);
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            return;
        }

        try
        {
            var error = _saveShortcut(_shortcut);
            if (error is null)
            {
                DialogResult = true;
                return;
            }

            SetInlineMessage(
                string.IsNullOrWhiteSpace(error) ? _localizer.Get(AppText.ShortcutSaveFailed) : error,
                isError: true);
        }
        catch
        {
            SetInlineMessage(_localizer.Get(AppText.ShortcutSaveFailed), isError: true);
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            CancelCapture();
        }

        DialogResult = false;
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturing)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }

            return;
        }

        e.Handled = true;
        if (e.IsRepeat)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (key is Key.None or Key.System || IsModifierKey(key))
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var modifiers = (uint)Keyboard.Modifiers;
        var candidate = new CorrectionShortcut(modifiers, unchecked((uint)virtualKey));
        _isCapturing = false;
        SetCaptureControlsEnabled(true);

        if (!candidate.IsValid)
        {
            SetInlineMessage(_localizer.Get(AppText.InvalidShortcut), isError: true);
            return;
        }

        _shortcut = candidate;
        _inlineMessage = null;
        ShowInlineMessage(null, isError: false);
        UpdateShortcutDisplay();
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift;

    private void CancelCapture()
    {
        if (!_isCapturing)
        {
            return;
        }

        _isCapturing = false;
        SetCaptureControlsEnabled(true);
        SetInlineMessage(null, isError: false);
        Keyboard.Focus(RecordButton);
    }

    private void SetCaptureControlsEnabled(bool enabled)
    {
        RecordButton.IsEnabled = enabled;
        RestoreButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        SaveButton.IsDefault = enabled;
    }

    private void SetInlineMessage(string? message, bool isError)
    {
        _inlineMessage = message;
        _inlineMessageIsError = isError;
        ShowInlineMessage(message, isError);
    }

    private void ShowInlineMessage(string? message, bool isError)
    {
        InlineMessageText.Text = message ?? string.Empty;
        InlineMessageText.Foreground = isError
            ? System.Windows.Media.Brushes.Firebrick
            : System.Windows.Media.Brushes.DimGray;
        InlineMessageText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}