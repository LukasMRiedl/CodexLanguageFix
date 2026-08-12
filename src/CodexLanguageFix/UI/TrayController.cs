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
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly AutostartService _autostartService;
    private bool _suppressAutostartEvent;

    public TrayController(AppSettings settings, SettingsService settingsService, AutostartService autostartService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _autostartService = autostartService;

        _enabledItem = new ToolStripMenuItem("Aktiviert")
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

        _autostartItem = new ToolStripMenuItem("Mit Windows starten")
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
                ShowMessage("Autostart konnte nicht geändert werden", exception.Message, ToolTipIcon.Error);
            }
        };

        var connectionItem = new ToolStripMenuItem("LanguageTool-Verbindung testen");
        connectionItem.Click += (_, _) => ConnectionTestRequested?.Invoke(this, EventArgs.Empty);
        var exitItem = new ToolStripMenuItem("Beenden");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _enabledItem,
            _autostartItem,
            new ToolStripSeparator(),
            connectionItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            Text = "Codex Language Fix",
            Icon = SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    public event EventHandler? EnabledChanged;
    public event EventHandler? ConnectionTestRequested;
    public event EventHandler? ExitRequested;

    public bool Enabled => _settings.Enabled;

    public void ShowMessage(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(4000, title, text, icon);
    }

    private void Save() => _settingsService.Save(_settings);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
