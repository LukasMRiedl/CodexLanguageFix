using System.Windows.Threading;
using System.Diagnostics;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.UI;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Core;

public sealed class CorrectionCoordinator : IDisposable
{
    private readonly IComposerAccessor _composerAccessor;
    private readonly IReadOnlyDictionary<CorrectionProviderKind, ICorrectionProvider> _providers;
    private readonly IOpenAiConnection _openAiConnection;
    private readonly OverlayWindow _overlay;
    private readonly TrayController _tray;
    private readonly DiagnosticLogger _logger;
    private readonly CodexFocusWatcher _focusWatcher;
    private readonly CorrectionHotkey _hotkey;
    private bool _shortcutFailureReported;
    private readonly DispatcherTimer _fallbackTimer;
    private readonly AppLocalizer _localizer;
    private readonly CorrectionPreparation _preparation;
    private CancellationTokenSource? _requestCancellation;
    private ComposerSnapshot? _visibleSnapshot;
    private UndoState? _undo;
    private bool _busy;
    private bool _disposed;
    private ComposerSnapshot? _requestSnapshot;
    private string? _transientMessage;
    private DateTimeOffset _transientUntil;
    private string? _lastCaptureDiagnostic;
    private string? _lastPlacementDiagnostic;

    public CorrectionCoordinator(
        IComposerAccessor composerAccessor,
        IEnumerable<ICorrectionProvider> providers,
        IOpenAiConnection openAiConnection,
        OverlayWindow overlay,
        TrayController tray,
        DiagnosticLogger logger,
        Dispatcher dispatcher,
        AppLocalizer localizer)
    {
        _composerAccessor = composerAccessor;
        _providers = providers.ToDictionary(provider => provider.Kind);
        _openAiConnection = openAiConnection;
        _overlay = overlay;
        _tray = tray;
        _logger = logger;
        _localizer = localizer;
        _preparation = new CorrectionPreparation(_providers[CorrectionProviderKind.Luna]);
        _preparation.Diagnostic += (eventName, length, elapsed) =>
            _logger.Write(eventName, length, elapsedMilliseconds: elapsed.TotalMilliseconds, provider: CorrectionProviderKind.Luna);
        _hotkey = new CorrectionHotkey();
        _hotkey.CorrectRequested += async (_, _) => await CorrectAsync(fromShortcut: true);
        _focusWatcher = new CodexFocusWatcher(dispatcher);
        _focusWatcher.Changed += FocusWatcher_OnChanged;
        _fallbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background, (_, _) => RefreshOverlay(), dispatcher);
        _overlay.CorrectRequested += async (_, _) => await CorrectAsync();
        _overlay.UndoRequested += (_, _) => Undo();
        _tray.EnabledChanged += (_, _) => { _requestCancellation?.Cancel(); RefreshOverlay(); };
        _tray.ConnectionTestRequested += async (_, _) => await TestConnectionAsync();
        _tray.ConnectOpenAiRequested += async (_, _) => await ConnectOpenAiAsync();
        _tray.ProviderChanged += Tray_OnProviderChanged;
        _tray.PreparationChanged += Tray_OnPreparationChanged;
        _localizer.LanguageChanged += Localizer_OnLanguageChanged;
    }

    public void Start()
    {
        _fallbackTimer.Start();
        RefreshOverlay();
    }

    private void FocusWatcher_OnChanged(object? sender, EventArgs e)
    {
        _composerAccessor.InvalidateLayout();
        RefreshOverlay();
    }

    private void RefreshOverlay()
    {
        if (_disposed) return;
        try
        {
            RefreshOverlayCore();
        }
        catch (Exception)
        {
            _preparation.Clear();
            _visibleSnapshot = null;
            _hotkey.SetEnabled(false);
            _overlay.Hide();
            _logger.Write("overlay_refresh_failed");
        }
    }

    private void RefreshOverlayCore()
    {
        if (!_tray.Enabled)
        {
            _preparation.Clear();
            _composerAccessor.InvalidateLayout();
            _requestCancellation?.Cancel();
            _visibleSnapshot = null;
            _hotkey.SetEnabled(false);
            _overlay.Hide();
            return;
        }

        var snapshot = _composerAccessor.TryPollFocusedComposer();
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Text))
        {
            _preparation.Clear();
            LogCaptureStatus();
            var hiddenDiagnostic = snapshot is null ? "unavailable" : $"{snapshot.Host}:{snapshot.FieldCategory}:empty_field";
            if (_lastPlacementDiagnostic != hiddenDiagnostic && snapshot is not null)
                _logger.Write("overlay_empty_field", host: snapshot.Host, fieldCategory: snapshot.FieldCategory, reason: "empty_field");
            _lastPlacementDiagnostic = hiddenDiagnostic;
            _visibleSnapshot = null;
            _hotkey.SetEnabled(false);
            _requestCancellation?.Cancel();
            _overlay.Hide();
            return;
        }

        if (_requestSnapshot is not null && !IsUnchangedEditor(_requestSnapshot, snapshot))
            _requestCancellation?.Cancel();
        if (_visibleSnapshot is not null && !_visibleSnapshot.SameEditor(snapshot))
            _transientMessage = null;
        _visibleSnapshot = snapshot;
        var shortcutEligible = _composerAccessor is CodexComposerAccessor { LastCaptureWasKeyboardFocused: true };
        _preparation.Observe(snapshot,
            shortcutEligible && _tray.PrepareLunaCorrections && _tray.CurrentProvider == CorrectionProviderKind.Luna,
            allowStart: !_busy);
        if (!_hotkey.SetEnabled(shortcutEligible) && !_shortcutFailureReported)
        {
            _shortcutFailureReported = true;
            _logger.Write("shortcut_registration_failed");
            _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable),
                _localizer.Get(AppText.ShortcutUnavailable), System.Windows.Forms.ToolTipIcon.Warning);
        }
        var canUndo = _undo is not null
            && _undo.Snapshot.SameEditor(snapshot)
            && string.Equals(snapshot.Text, _undo.Corrected, StringComparison.Ordinal);

        var message = DateTimeOffset.UtcNow < _transientUntil ? _transientMessage : null;
        _overlay.ShowStatus(message ?? (_busy ? _localizer.Get(AppText.Checking)
            : canUndo ? FormatChangeCount(_undo!.ChangeCount, _undo.Provider, _localizer)
            : _preparation.IsReady(snapshot) ? _localizer.Get(AppText.CorrectionReady) : null), canUndo && !_busy);
        PositionOverlay(snapshot);
    }

    private async Task CorrectAsync(bool fromShortcut = false)
    {
        var commandStopwatch = Stopwatch.StartNew();
        if (_busy || _disposed || !_tray.Enabled)
        {
            return;
        }

        var captureStopwatch = Stopwatch.StartNew();
        var snapshot = _composerAccessor.TryCaptureFocusedComposerWithoutLayout(fromShortcut);
        captureStopwatch.Stop();
        LogCaptureStatus();
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Text))
        {
            if (fromShortcut)
            {
                _hotkey.SetEnabled(false);
                return;
            }
            ShowTransient(_localizer.Get(AppText.NoPromptDetected));
            return;
        }

        _busy = true;
        _requestSnapshot = snapshot;
        _transientMessage = null;
        _overlay.SetBusy(true);
        _overlay.ShowStatus(_localizer.Get(AppText.Checking));
        _requestCancellation = new CancellationTokenSource();
        var requestCancellation = _requestCancellation;
        var requestProvider = GetCurrentProvider();
        var commandStatus = "failed";
        try
        {
            var provider = requestProvider;
            _logger.Write("correction_capture_completed", snapshot.Text.Length,
                elapsedMilliseconds: captureStopwatch.Elapsed.TotalMilliseconds, provider: provider.Kind);
            _logger.Write("check_started", snapshot.Text.Length, provider: provider.Kind);
            var providerStopwatch = Stopwatch.StartNew();
            var prepared = _preparation.BeginManual(snapshot);
            if (provider.Kind == CorrectionProviderKind.Luna && prepared is not null)
            {
                var preparationSucceeded = await prepared.WaitAsync(requestCancellation.Token);
                _logger.Write(preparationSucceeded ? "preparation_adopted" : "preparation_unavailable",
                    snapshot.Text.Length, provider: provider.Kind);
            }
            var outcome = await provider.CorrectAsync(snapshot.Text, requestCancellation.Token);
            providerStopwatch.Stop();
            _logger.Write("correction_provider_completed", snapshot.Text.Length,
                elapsedMilliseconds: providerStopwatch.Elapsed.TotalMilliseconds, provider: provider.Kind,
                reason: outcome.CacheHit ? "cache_hit" : "fresh_result");
            requestCancellation.Token.ThrowIfCancellationRequested();
            var verificationStopwatch = Stopwatch.StartNew();
            var active = _composerAccessor.TryCaptureFocusedComposerWithoutLayout();
            if (active is null || !IsUnchangedEditor(snapshot, active))
                throw new LanguageFixException(_localizer.Get(AppText.PromptChanged));
            _logger.Write("check_completed", snapshot.Text.Length, outcome.ChangeCount, outcome.StatusCode, outcome.Elapsed.TotalMilliseconds, provider.Kind);

            if (string.Equals(outcome.CorrectedText, snapshot.Text, StringComparison.Ordinal))
            {
                commandStatus = "unchanged";
                commandStopwatch.Stop();
                ShowTransient(provider.Kind == CorrectionProviderKind.Luna
                    ? _localizer.Get(AppText.NoChangesFoundProvider, ProviderName(provider.Kind))
                    : _localizer.Get(AppText.NoChangesFound), snapshot);
                return;
            }

            var current = _composerAccessor.TryRefresh(snapshot);
            if (current is null)
            {
                _logger.Write("composer_unavailable_before_write", snapshot.Text.Length);
                throw new LanguageFixException(_localizer.Get(AppText.ComposerUnavailable));
            }

            if (!IsUnchangedEditor(snapshot, current))
            {
                _logger.Write("composer_changed_before_write", snapshot.Text.Length);
                throw new LanguageFixException(_localizer.Get(AppText.PromptChanged));
            }

            verificationStopwatch.Stop();
            _logger.Write("correction_verification_completed", snapshot.Text.Length,
                elapsedMilliseconds: verificationStopwatch.Elapsed.TotalMilliseconds, provider: provider.Kind);

            var composerStopwatch = Stopwatch.StartNew();
            var replaced = _composerAccessor.TryReplace(current, snapshot.Text, outcome.CorrectedText);
            composerStopwatch.Stop();
            if (outcome.LunaExecution is { } lunaExecution)
            {
                outcome = outcome with
                {
                    LunaExecution = lunaExecution with
                    {
                        Timings = lunaExecution.Timings with { ComposerWrite = composerStopwatch.Elapsed }
                    }
                };
            }

            if (!replaced)
            {
                var detail = _composerAccessor is CodexComposerAccessor accessor
                    ? accessor.LastWriteStatus
                    : "unknown";
                _logger.Write($"composer_write_rejected_{detail}", snapshot.Text.Length);
                throw new LanguageFixException(_localizer.Get(AppText.WriteRejected));
            }

            commandStatus = "applied";
            commandStopwatch.Stop();
            _preparation.Suppress(current with { Text = outcome.CorrectedText });

            _logger.Write(
                "composer_write_completed",
                snapshot.Text.Length,
                outcome.ChangeCount,
                outcome.StatusCode,
                composerStopwatch.Elapsed.TotalMilliseconds,
                provider.Kind);

            _undo = new UndoState(current, snapshot.Text, outcome.CorrectedText, outcome.ChangeCount, outcome.Provider);
            _overlay.ShowStatus(FormatChangeCount(outcome.ChangeCount, outcome.Provider, _localizer), true);
            _tray.ShowMessage(
                _localizer.Get(AppText.PromptCorrected),
                outcome.ChangeCount == 1
                    ? _localizer.Get(AppText.CorrectionAppliedOne, ProviderName(outcome.Provider))
                    : _localizer.Get(AppText.CorrectionsAppliedMany, outcome.ChangeCount, ProviderName(outcome.Provider)));
        }
        catch (OperationCanceledException)
        {
            commandStopwatch.Stop();
            commandStatus = "cancelled";
            _logger.Write("check_cancelled", snapshot.Text.Length);
            ShowTransient(_localizer.Get(AppText.CheckCancelled), snapshot);
        }
        catch (LanguageFixException exception)
        {
            commandStopwatch.Stop();
            _logger.Write("check_failed", snapshot.Text.Length);
            ShowTransient(exception.Message, snapshot);
            if (!_disposed) _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), exception.Message, System.Windows.Forms.ToolTipIcon.Warning);
        }
        catch (Exception exception)
        {
            commandStopwatch.Stop();
            _logger.Write("unexpected_check_failure", snapshot.Text.Length);
            var message = _localizer.Get(AppText.UnexpectedCorrectionFailure);
            ShowTransient(message, snapshot);
            if (!_disposed) _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), message, System.Windows.Forms.ToolTipIcon.Error);
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            _logger.Write("correction_command_completed", snapshot.Text.Length,
                elapsedMilliseconds: commandStopwatch.Elapsed.TotalMilliseconds, provider: requestProvider.Kind,
                reason: $"{(fromShortcut ? "shortcut" : "button")}_{commandStatus}");
            requestCancellation.Dispose();
            _requestCancellation = null;
            _busy = false;
            _requestSnapshot = null;
            if (!_disposed)
            {
                _overlay.SetBusy(false);
                _ = _overlay.Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshOverlay);
            }
        }
    }

    private void Undo()
    {
        if (_busy || _undo is null)
        {
            return;
        }

        var current = _composerAccessor.TryCaptureFocusedComposerWithoutLayout();
        if (current is null || !_undo.Snapshot.SameEditor(current)
            || !string.Equals(current.Text, _undo.Corrected, StringComparison.Ordinal))
        {
            ShowTransient(_localizer.Get(AppText.UndoUnavailable));
            return;
        }

        if (_composerAccessor.TryReplace(current, current.Text, _undo.Original))
        {
            _preparation.Suppress(current with { Text = _undo.Original });
            _logger.Write("composer_undo_completed", current.Text.Length);
            _undo = null;
            ShowTransient(_localizer.Get(AppText.UndoCompleted));
        }
        else
        {
            var detail = _composerAccessor is CodexComposerAccessor accessor ? accessor.LastWriteStatus : "unknown";
            _logger.Write($"composer_undo_rejected_{detail}", current.Text.Length);
            ShowTransient(_localizer.Get(AppText.UndoFailed));
        }
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            var provider = GetCurrentProvider();
            var result = await provider.TestAsync(CancellationToken.None);
            _tray.ShowMessage(
                _localizer.Get(AppText.ProviderAvailable, ProviderName(provider.Kind)),
                _localizer.Get(AppText.ResponseTime, result.Elapsed.TotalMilliseconds));
        }
        catch (LanguageFixException exception)
        {
            _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), exception.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
        catch (Exception)
        {
            _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), _localizer.Get(AppText.UnexpectedConnectionFailure), System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private async Task ConnectOpenAiAsync()
    {
        try
        {
            await _openAiConnection.ConnectAsync(CancellationToken.None);
            _tray.ShowMessage(_localizer.Get(AppText.OpenAiConnected), _localizer.Get(AppText.ProviderAvailable, ProviderName(CorrectionProviderKind.Luna)));
        }
        catch (LanguageFixException exception)
        {
            _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), exception.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
        catch (Exception)
        {
            _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), _localizer.Get(AppText.UnexpectedConnectionFailure), System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private async void Tray_OnProviderChanged(object? sender, EventArgs e)
    {
        _preparation.Clear();
        _requestCancellation?.Cancel();
        RefreshOverlay();
        if (_tray.CurrentProvider != CorrectionProviderKind.Luna)
        {
            return;
        }

        try
        {
            await _openAiConnection.WarmUpAsync(CancellationToken.None);
            _logger.Write("luna_warmup_completed", provider: CorrectionProviderKind.Luna);
        }
        catch (Exception)
        {
            _logger.Write("luna_warmup_failed", provider: CorrectionProviderKind.Luna);
        }
    }

    private void Tray_OnPreparationChanged(object? sender, EventArgs e)
    {
        _preparation.Clear();
        RefreshOverlay();
    }

    private ICorrectionProvider GetCurrentProvider()
    {
        if (_providers.TryGetValue(_tray.CurrentProvider, out var provider))
        {
            return provider;
        }

        return _providers[CorrectionProviderKind.LanguageTool];
    }

    private string ProviderName(CorrectionProviderKind provider) => provider == CorrectionProviderKind.Luna
        ? "Luna"
        : _localizer.Get(AppText.ProviderLanguageTool);

    private void ShowTransient(string message, ComposerSnapshot? origin = null)
    {
        if (_disposed || (origin is not null && !origin.SameEditor(_composerAccessor.TryCaptureFocusedComposerWithoutLayout())))
            return;
        _transientMessage = message;
        _transientUntil = DateTimeOffset.UtcNow.AddSeconds(6);
        RefreshOverlay();
    }

    private void PositionOverlay(ComposerSnapshot snapshot)
    {
        _overlay.PositionAt(snapshot.Bounds, snapshot.RightControlBounds, snapshot.Host,
            snapshot.EditorBounds, snapshot.OccupiedBounds, snapshot.PlacementBounds, snapshot.EditorTextBounds);
        LogCaptureStatus();
        var diagnostic = $"{snapshot.Host}:{snapshot.FieldCategory}:{_overlay.PlacementStatus}";
        if (_lastPlacementDiagnostic != diagnostic)
        {
            _lastPlacementDiagnostic = diagnostic;
            _logger.Write($"overlay_{_overlay.PlacementStatus}", host: snapshot.Host,
                fieldCategory: snapshot.FieldCategory, reason: _overlay.PlacementStatus);
        }
    }

    private void LogCaptureStatus()
    {
        if (_composerAccessor is CodexComposerAccessor accessor)
        {
            var diagnostic = $"{accessor.LastCaptureHost}:{accessor.LastFieldCategory}:{accessor.LastCaptureStatus}";
            if (_lastCaptureDiagnostic == diagnostic) return;
            _lastCaptureDiagnostic = diagnostic;
            _logger.Write($"composer_capture_{accessor.LastCaptureStatus}", host: accessor.LastCaptureHost,
                fieldCategory: accessor.LastFieldCategory, reason: accessor.LastCaptureStatus);
        }
    }

    internal static bool IsUnchangedEditor(ComposerSnapshot expected, ComposerSnapshot observed) =>
        expected.SameEditor(observed) && string.Equals(expected.Text, observed.Text, StringComparison.Ordinal);

    private void Localizer_OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshOverlay();
    }

    internal static string FormatChangeCount(int count, CorrectionProviderKind provider, AppLocalizer localizer)
    {
        var value = count == 1
            ? localizer.Get(AppText.ChangeCountOne)
            : localizer.Get(AppText.ChangeCountMany, count);
        return provider == CorrectionProviderKind.Luna ? $"{value} · Luna" : value;
    }

    internal static string FormatChangeCount(int count, AppLocalizer localizer) =>
        FormatChangeCount(count, CorrectionProviderKind.LanguageTool, localizer);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preparation.Dispose();
        _requestCancellation?.Cancel();
        _fallbackTimer.Stop();
        _focusWatcher.Changed -= FocusWatcher_OnChanged;
        _focusWatcher.Dispose();
        _hotkey.Dispose();
        _composerAccessor.InvalidateLayout();
        _visibleSnapshot = null;
        _requestSnapshot = null;
        _undo = null;
        _tray.ProviderChanged -= Tray_OnProviderChanged;
        _tray.PreparationChanged -= Tray_OnPreparationChanged;
        _localizer.LanguageChanged -= Localizer_OnLanguageChanged;
        _overlay.Close();
    }

    private sealed record UndoState(
        ComposerSnapshot Snapshot,
        string Original,
        string Corrected,
        int ChangeCount,
        CorrectionProviderKind Provider);
}
