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
    private CodexAppServerClient? _codexAppServerClient;
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
        var localizer = new AppLocalizer(settings.Language);
        var autostartService = new AutostartService(localizer);
        var logger = new DiagnosticLogger(settingsService.ApplicationDirectory);

        if (!settings.FirstRunNoticeShown)
        {
            var notice = new FirstRunWindow(localizer);
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

        _languageToolClient = new LanguageToolClient(localizer: localizer);
        _codexAppServerClient = new CodexAppServerClient(settingsService.ApplicationDirectory, localizer);
        if (settings.CorrectionProvider == CorrectionProviderKind.Luna)
        {
            _ = WarmUpLunaAsync(_codexAppServerClient, logger);
        }
        var correctionEngine = new CorrectionEngine();
        var languageToolProvider = new LanguageToolCorrectionProvider(_languageToolClient, correctionEngine);
        var lunaProvider = new LunaCorrectionProvider(_codexAppServerClient, localizer);
        _tray = new TrayController(settings, settingsService, autostartService, localizer);
        var overlay = new OverlayWindow(localizer);
        _coordinator = new CorrectionCoordinator(
            new CodexComposerAccessor(),
            [languageToolProvider, lunaProvider],
            lunaProvider,
            overlay,
            _tray,
            logger,
            Dispatcher,
            localizer);
        _tray.ExitRequested += (_, _) => Shutdown();
        _coordinator.Start();
        logger.Write("application_started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        _tray?.Dispose();
        _languageToolClient?.Dispose();
        _codexAppServerClient?.Dispose();
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

    private static async Task WarmUpLunaAsync(CodexAppServerClient client, DiagnosticLogger logger)
    {
        try
        {
            await client.WarmUpAsync(CancellationToken.None).ConfigureAwait(false);
            logger.Write("luna_warmup_completed", provider: CorrectionProviderKind.Luna);
        }
        catch (Exception)
        {
            logger.Write("luna_warmup_failed", provider: CorrectionProviderKind.Luna);
        }
    }
}
