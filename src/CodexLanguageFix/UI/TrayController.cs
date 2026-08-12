using System.Drawing;
using System.Windows.Forms;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.UI;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _connectionItem;
    private readonly ToolStripMenuItem _languageItem;
    private readonly ToolStripMenuItem _automaticLanguageItem;
    private readonly ToolStripMenuItem _germanLanguageItem;
    private readonly ToolStripMenuItem _englishLanguageItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly AutostartService _autostartService;
    private readonly AppLocalizer _localizer;
    private bool _suppressAutostartEvent;

    public TrayController(
        AppSettings settings,
        SettingsService settingsService,
        AutostartService autostartService,
        AppLocalizer localizer)
    {
        _settings = settings;
        _settingsService = settingsService;
        _autostartService = autostartService;
        _localizer = localizer;

        _enabledItem = new ToolStripMenuItem
        {
            Checked = settings.Enabled,
            CheckOnClick = true
        };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            _settings.Enabled = _enabledItem.Checked;
            Save();
            EnabledChanged?.Invoke(this, EventArgs.Empty);
        };

        _autostartItem = new ToolStripMenuItem
        {
            Checked = settings.StartWithWindows,
            CheckOnClick = true
        };
        _autostartItem.CheckedChanged += (_, _) =>
        {
            if (_suppressAutostartEvent)
            {
                return;
            }

            try
            {
                _autostartService.SetEnabled(_autostartItem.Checked);
                _settings.StartWithWindows = _autostartItem.Checked;
                Save();
            }
            catch (Exception exception)
            {
                _suppressAutostartEvent = true;
                _autostartItem.Checked = !_autostartItem.Checked;
                _suppressAutostartEvent = false;
                ShowMessage(_localizer.Get(AppText.AutostartChangeFailed), exception.Message, ToolTipIcon.Error);
            }
        };

        _connectionItem = new ToolStripMenuItem();
        _connectionItem.Click += (_, _) => ConnectionTestRequested?.Invoke(this, EventArgs.Empty);
        _languageItem = new ToolStripMenuItem();
        _automaticLanguageItem = CreateLanguageItem(AppLocalizer.Automatic);
        _germanLanguageItem = CreateLanguageItem(AppLocalizer.German);
        _englishLanguageItem = CreateLanguageItem(AppLocalizer.English);
        _languageItem.DropDownItems.AddRange([_automaticLanguageItem, _germanLanguageItem, _englishLanguageItem]);
        _exitItem = new ToolStripMenuItem();
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _enabledItem,
            _autostartItem,
            new ToolStripSeparator(),
            _connectionItem,
            _languageItem,
            new ToolStripSeparator(),
            _exitItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            Text = "Codex Language Fix",
            Icon = SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true
        };
        _localizer.LanguageChanged += Localizer_OnLanguageChanged;
        ApplyLanguage();
    }

    public event EventHandler? EnabledChanged;
    public event EventHandler? ConnectionTestRequested;
    public event EventHandler? ExitRequested;

    public bool Enabled => _settings.Enabled;

    private ToolStripMenuItem CreateLanguageItem(string language)
    {
        var item = new ToolStripMenuItem { Tag = language };
        item.Click += (_, _) =>
        {
            _settings.Language = language;
            _localizer.SetLanguage(language);
            Save();
            UpdateLanguageChecks();
        };
        return item;
    }

    private void Localizer_OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        _enabledItem.Text = _localizer.Get(AppText.Enabled);
        _autostartItem.Text = _localizer.Get(AppText.StartWithWindows);
        _connectionItem.Text = _localizer.Get(AppText.TestConnection);
        _languageItem.Text = _localizer.Get(AppText.Language);
        _automaticLanguageItem.Text = _localizer.Get(AppText.LanguageAutomatic);
        _germanLanguageItem.Text = _localizer.Get(AppText.LanguageGerman);
        _englishLanguageItem.Text = _localizer.Get(AppText.LanguageEnglish);
        _exitItem.Text = _localizer.Get(AppText.Exit);
        UpdateLanguageChecks();
    }

    private void UpdateLanguageChecks()
    {
        _automaticLanguageItem.Checked = _localizer.LanguageMode == AppLocalizer.Automatic;
        _germanLanguageItem.Checked = _localizer.LanguageMode == AppLocalizer.German;
        _englishLanguageItem.Checked = _localizer.LanguageMode == AppLocalizer.English;
    }

    public void ShowMessage(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(4000, title, text, icon);
    }

    private void Save() => _settingsService.Save(_settings);

    public void Dispose()
    {
        _localizer.LanguageChanged -= Localizer_OnLanguageChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
