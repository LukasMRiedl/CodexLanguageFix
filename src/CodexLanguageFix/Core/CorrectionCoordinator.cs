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
    private readonly DispatcherTimer _fallbackTimer;
    private readonly AppLocalizer _localizer;
    private CancellationTokenSource? _requestCancellation;
    private ComposerSnapshot? _visibleSnapshot;
    private UndoState? _undo;
    private bool _busy;
    private bool _disposed;
    private ComposerSnapshot? _requestSnapshot;
    private string? _transientMessage;
    private DateTimeOffset _transientUntil;

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
        _focusWatcher = new CodexFocusWatcher(dispatcher);
        _focusWatcher.Changed += (_, _) => RefreshOverlay();
        _fallbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background, (_, _) => RefreshOverlay(), dispatcher);
        _overlay.CorrectRequested += async (_, _) => await CorrectAsync();
        _overlay.UndoRequested += (_, _) => Undo();
        _tray.EnabledChanged += (_, _) => { _requestCancellation?.Cancel(); RefreshOverlay(); };
        _tray.ConnectionTestRequested += async (_, _) => await TestConnectionAsync();
        _tray.ConnectOpenAiRequested += async (_, _) => await ConnectOpenAiAsync();
        _tray.ProviderChanged += Tray_OnProviderChanged;
        _localizer.LanguageChanged += Localizer_OnLanguageChanged;
    }

    public void Start()
    {
        _fallbackTimer.Start();
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
            _visibleSnapshot = null;
            _overlay.Hide();
            _logger.Write("overlay_refresh_failed");
        }
    }

    private void RefreshOverlayCore()
    {
        if (!_tray.Enabled)
        {
            _requestCancellation?.Cancel();
            _visibleSnapshot = null;
            _overlay.Hide();
            return;
        }

        var snapshot = _composerAccessor.TryCaptureFocusedComposer();
        if (snapshot is null)
        {
            _visibleSnapshot = null;
            _requestCancellation?.Cancel();
            _overlay.Hide();
            return;
        }

        if (_requestSnapshot is not null && !IsUnchangedEditor(_requestSnapshot, snapshot))
            _requestCancellation?.Cancel();
        if (_visibleSnapshot is not null && !_visibleSnapshot.SameEditor(snapshot))
            _transientMessage = null;
        _visibleSnapshot = snapshot;
        var canUndo = _undo is not null
            && _undo.Snapshot.SameEditor(snapshot)
            && string.Equals(snapshot.Text, _undo.Corrected, StringComparison.Ordinal);

        var message = DateTimeOffset.UtcNow < _transientUntil ? _transientMessage : null;
        _overlay.ShowStatus(message ?? (_busy ? _localizer.Get(AppText.Checking)
            : canUndo ? FormatChangeCount(_undo!.ChangeCount, _undo.Provider, _localizer) : null), canUndo && !_busy);
        PositionOverlay(snapshot);
    }

    private async Task CorrectAsync()
    {
        if (_busy || _disposed)
        {
            return;
        }

        var snapshot = _composerAccessor.TryCaptureFocusedComposer();
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Text))
        {
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
        try
        {
            var provider = GetCurrentProvider();
            var outcome = await provider.CorrectAsync(snapshot.Text, requestCancellation.Token);
            requestCancellation.Token.ThrowIfCancellationRequested();
            var active = _composerAccessor.TryCaptureFocusedComposer();
            if (active is null || !IsUnchangedEditor(snapshot, active))
                throw new LanguageFixException(_localizer.Get(AppText.PromptChanged));
            _logger.Write("check_completed", snapshot.Text.Length, outcome.ChangeCount, outcome.StatusCode, outcome.Elapsed.TotalMilliseconds, provider.Kind);

            if (string.Equals(outcome.CorrectedText, snapshot.Text, StringComparison.Ordinal))
            {
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
                if (_composerAccessor.LastWriteResult?.State == ComposerWriteState.PartiallyApplied)
                    new RecoveryWindow(snapshot.Text, _localizer).Show();
                var detail = _composerAccessor is CodexComposerAccessor accessor
                    ? accessor.LastWriteStatus
                    : "unknown";
                _logger.Write($"composer_write_rejected_{detail}", snapshot.Text.Length);
                throw new LanguageFixException(_localizer.Get(AppText.WriteRejected));
            }

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
            ShowTransient(_localizer.Get(AppText.CheckCancelled), snapshot);
        }
        catch (LanguageFixException exception)
        {
            _logger.Write("check_failed", snapshot.Text.Length);
            ShowTransient(exception.Message, snapshot);
            if (!_disposed) _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), exception.Message, System.Windows.Forms.ToolTipIcon.Warning);
        }
        catch (Exception exception)
        {
            _logger.Write("unexpected_check_failure", snapshot.Text.Length);
            var message = _localizer.Get(AppText.UnexpectedCorrectionFailure);
            ShowTransient(message, snapshot);
            if (!_disposed) _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), message, System.Windows.Forms.ToolTipIcon.Error);
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
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

        var current = _composerAccessor.TryCaptureFocusedComposer();
        if (current is null || !_undo.Snapshot.SameEditor(current)
            || !string.Equals(current.Text, _undo.Corrected, StringComparison.Ordinal))
        {
            ShowTransient(_localizer.Get(AppText.UndoUnavailable));
            return;
        }

        if (_composerAccessor.TryReplace(current, current.Text, _undo.Original))
        {
            _undo = null;
            ShowTransient(_localizer.Get(AppText.UndoCompleted));
        }
        else
        {
            if (_composerAccessor.LastWriteResult?.State == ComposerWriteState.PartiallyApplied)
                new RecoveryWindow(_undo.Original, _localizer).Show();
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
        if (_disposed || (origin is not null && !origin.SameEditor(_composerAccessor.TryCaptureFocusedComposer())))
            return;
        _transientMessage = message;
        _transientUntil = DateTimeOffset.UtcNow.AddSeconds(6);
        RefreshOverlay();
    }

    private void PositionOverlay(ComposerSnapshot snapshot) =>
        _overlay.PositionAt(snapshot.Bounds, snapshot.RightControlBounds, snapshot.Host,
            snapshot.EditorBounds, snapshot.OccupiedBounds);

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
        _requestCancellation?.Cancel();
        _fallbackTimer.Stop();
        _focusWatcher.Dispose();
        _tray.ProviderChanged -= Tray_OnProviderChanged;
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
