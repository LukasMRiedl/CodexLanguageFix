using System.Threading;
using System.Windows;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.UI;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private LanguageToolClient? _languageToolClient;
    private TrayController? _tray;
    private CorrectionCoordinator? _coordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, @"Local\CodexLanguageFix.SingleInstance", out var ownsMutex);
        if (!ownsMutex)
        {
            Shutdown();
            return;
        }

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var autostartService = new AutostartService();
        var logger = new DiagnosticLogger(settingsService.ApplicationDirectory);

        if (!settings.FirstRunNoticeShown)
        {
            var notice = new FirstRunWindow();
            if (notice.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            settings.FirstRunNoticeShown = true;
            settingsService.Save(settings);
        }

        if (settings.StartWithWindows && !autostartService.IsEnabled)
        {
            try
            {
                autostartService.SetEnabled(true);
            }
            catch (Exception)
            {
                settings.StartWithWindows = false;
                settingsService.Save(settings);
            }
        }

        _languageToolClient = new LanguageToolClient();
        _tray = new TrayController(settings, settingsService, autostartService);
        var overlay = new OverlayWindow();
        _coordinator = new CorrectionCoordinator(
            new CodexComposerAccessor(),
            _languageToolClient,
            new CorrectionEngine(),
            overlay,
            _tray,
            logger,
            Dispatcher);
        _tray.ExitRequested += (_, _) => Shutdown();
        _coordinator.Start();
        logger.Write("application_started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        _tray?.Dispose();
        _languageToolClient?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Die Instanz besaß den Mutex nicht mehr.
            }
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
