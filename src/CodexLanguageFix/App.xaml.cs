using System.Threading;
using System.Windows;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.UI;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix;

public partial class App : System.Windows.Application
{
    private DiagnosticLogger? _logger;
    private string _exitReason = "runtime_exit";
    private Mutex? _singleInstanceMutex;
    private LanguageToolClient? _languageToolClient;
    private CodexAppServerClient? _codexAppServerClient;
    private TrayController? _tray;
    private CorrectionCoordinator? _coordinator;
    private MemoryDiagnostics? _memoryDiagnostics;

    protected override void OnStartup(StartupEventArgs e)
    {
        var settingsService = new SettingsService();
        var logger = new DiagnosticLogger(settingsService.ApplicationDirectory);
        _logger = logger;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, @"Local\CodexLanguageFix.SingleInstance", out var ownsMutex);
        if (!ownsMutex)
        {
            _exitReason = "existing_instance";
            Shutdown();
            return;
        }

        var settings = settingsService.Load();
        var localizer = new AppLocalizer(settings.Language);
        var autostartService = new AutostartService(localizer);

        if (!settings.FirstRunNoticeShown)
        {
            var notice = new FirstRunWindow(localizer);
            if (notice.ShowDialog() != true)
            {
                _exitReason = "notice_declined";
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
        var composerAccessor = new CodexComposerAccessor();
        _memoryDiagnostics = new MemoryDiagnostics(composerAccessor);
        _coordinator = new CorrectionCoordinator(
            composerAccessor,
            [languageToolProvider, lunaProvider],
            lunaProvider,
            overlay,
            _tray,
            logger,
            Dispatcher,
            localizer);
        _tray.ExitRequested += (_, _) =>
        {
            _exitReason = "tray_exit";
            Shutdown();
        };
        _coordinator.Start();
        logger.Write("application_started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        _memoryDiagnostics?.Dispose();
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
        _logger?.Write("application_exit", statusCode: e.ApplicationExitCode, reason: _exitReason);
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _exitReason = "dispatcher_failure";
        _logger?.WriteFailure("application_dispatcher_failure", e.Exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _exitReason = "runtime_failure";
        if (e.ExceptionObject is Exception exception)
            _logger?.WriteFailure("application_runtime_failure", exception);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _exitReason = "session_ending";
        _logger?.Write("application_session_ending", reason: e.ReasonSessionEnding.ToString());
        base.OnSessionEnding(e);
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
