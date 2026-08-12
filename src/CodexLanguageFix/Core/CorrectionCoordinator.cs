using System.Windows.Threading;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.UI;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Core;

public sealed class CorrectionCoordinator : IDisposable
{
    private readonly IComposerAccessor _composerAccessor;
    private readonly ILanguageToolClient _languageToolClient;
    private readonly ICorrectionEngine _correctionEngine;
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

    public CorrectionCoordinator(
        IComposerAccessor composerAccessor,
        ILanguageToolClient languageToolClient,
        ICorrectionEngine correctionEngine,
        OverlayWindow overlay,
        TrayController tray,
        DiagnosticLogger logger,
        Dispatcher dispatcher,
        AppLocalizer localizer)
    {
        _composerAccessor = composerAccessor;
        _languageToolClient = languageToolClient;
        _correctionEngine = correctionEngine;
        _overlay = overlay;
        _tray = tray;
        _logger = logger;
        _localizer = localizer;
        _focusWatcher = new CodexFocusWatcher(dispatcher);
        _focusWatcher.Changed += (_, _) => RefreshOverlay();
        _fallbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background, (_, _) => RefreshOverlay(), dispatcher);
        _overlay.CorrectRequested += async (_, _) => await CorrectAsync();
        _overlay.UndoRequested += (_, _) => Undo();
        _tray.EnabledChanged += (_, _) => RefreshOverlay();
        _tray.ConnectionTestRequested += async (_, _) => await TestConnectionAsync();
        _localizer.LanguageChanged += Localizer_OnLanguageChanged;
    }

    public void Start()
    {
        _fallbackTimer.Start();
        RefreshOverlay();
    }

    private void RefreshOverlay()
    {
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
        if (!_tray.Enabled || _busy)
        {
            if (!_busy)
            {
                _overlay.Hide();
            }
            return;
        }

        var snapshot = _composerAccessor.TryCaptureFocusedComposer();
        if (snapshot is null)
        {
            _visibleSnapshot = null;
            _undo = null;
            _overlay.Hide();
            return;
        }

        _visibleSnapshot = snapshot;
        var canUndo = _undo is not null
            && RuntimeIdsEqual(_undo.Snapshot.RuntimeId, snapshot.RuntimeId)
            && string.Equals(snapshot.Text, _undo.Corrected, StringComparison.Ordinal);
        if (!canUndo)
        {
            _undo = null;
        }

        _overlay.ShowStatus(canUndo ? FormatChangeCount(_undo!.ChangeCount, _localizer) : null, canUndo);
        _overlay.PositionAt(snapshot.Bounds, snapshot.RightControlBounds, snapshot.Host);
    }

    private async Task CorrectAsync()
    {
        if (_busy)
        {
            return;
        }

        var snapshot = _composerAccessor.TryCaptureFocusedComposer() ?? _visibleSnapshot;
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Text))
        {
            ShowTransient(_localizer.Get(AppText.NoPromptDetected));
            return;
        }

        _busy = true;
        _overlay.SetBusy(true);
        _overlay.ShowStatus(_localizer.Get(AppText.Checking));
        _requestCancellation = new CancellationTokenSource();
        try
        {
            var annotated = _correctionEngine.Annotate(snapshot.Text);
            var check = await _languageToolClient.CheckAsync(annotated, _requestCancellation.Token);
            var outcome = _correctionEngine.Apply(snapshot.Text, annotated, check.Matches);
            _logger.Write("check_completed", snapshot.Text.Length, check.Matches.Count, check.StatusCode, check.Elapsed.TotalMilliseconds);

            if (!outcome.Changed || string.Equals(outcome.CorrectedText, snapshot.Text, StringComparison.Ordinal))
            {
                ShowTransient(_localizer.Get(AppText.NoChangesFound));
                return;
            }

            var current = _composerAccessor.TryRefresh(snapshot);
            if (current is null)
            {
                _logger.Write("composer_unavailable_before_write", snapshot.Text.Length);
                throw new LanguageFixException(_localizer.Get(AppText.ComposerUnavailable));
            }

            if (!string.Equals(current.Text, snapshot.Text, StringComparison.Ordinal))
            {
                _logger.Write("composer_changed_before_write", snapshot.Text.Length);
                throw new LanguageFixException(_localizer.Get(AppText.PromptChanged));
            }

            if (!_composerAccessor.TryReplace(current, snapshot.Text, outcome.CorrectedText))
            {
                var detail = _composerAccessor is CodexComposerAccessor accessor
                    ? accessor.LastWriteStatus
                    : "unknown";
                _logger.Write($"composer_write_rejected_{detail}", snapshot.Text.Length);
                throw new LanguageFixException(_localizer.Get(AppText.WriteRejected));
            }

            _undo = new UndoState(current, snapshot.Text, outcome.CorrectedText, outcome.Corrections.Count);
            _overlay.ShowStatus(FormatChangeCount(outcome.Corrections.Count, _localizer), true);
            _tray.ShowMessage(
                _localizer.Get(AppText.PromptCorrected),
                outcome.Corrections.Count == 1
                    ? _localizer.Get(AppText.SuggestionAppliedOne)
                    : _localizer.Get(AppText.SuggestionsAppliedMany, outcome.Corrections.Count));
        }
        catch (OperationCanceledException)
        {
            ShowTransient(_localizer.Get(AppText.CheckCancelled));
        }
        catch (LanguageFixException exception)
        {
            _logger.Write("check_failed", snapshot.Text.Length);
            ShowTransient(exception.Message);
            _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), exception.Message, System.Windows.Forms.ToolTipIcon.Warning);
        }
        catch (Exception exception)
        {
            _logger.Write("unexpected_check_failure", snapshot.Text.Length);
            var message = _localizer.Get(AppText.UnexpectedCorrectionFailure);
            ShowTransient(message);
            _tray.ShowMessage(_localizer.Get(AppText.CorrectionUnavailable), message, System.Windows.Forms.ToolTipIcon.Error);
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
            _overlay.SetBusy(false);
            _ = _overlay.Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshOverlay);
        }
    }

    private void Undo()
    {
        if (_busy || _undo is null)
        {
            return;
        }

        var current = _composerAccessor.TryCaptureFocusedComposer()
            ?? _composerAccessor.TryRefresh(_undo.Snapshot);
        if (current is null || !MatchesProviderText(current.Text, _undo.Corrected))
        {
            _undo = null;
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
            ShowTransient(_localizer.Get(AppText.UndoFailed));
        }
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            var prompt = _correctionEngine.Annotate("This is a connection test.");
            var result = await _languageToolClient.CheckAsync(prompt, CancellationToken.None);
            _tray.ShowMessage(_localizer.Get(AppText.LanguageToolAvailable), _localizer.Get(AppText.ResponseTime, result.Elapsed.TotalMilliseconds));
        }
        catch (LanguageFixException exception)
        {
            _tray.ShowMessage(_localizer.Get(AppText.LanguageToolUnavailable), exception.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
        catch (Exception)
        {
            _tray.ShowMessage(_localizer.Get(AppText.LanguageToolUnavailable), _localizer.Get(AppText.UnexpectedConnectionFailure), System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private void ShowTransient(string message)
    {
        _overlay.ShowStatus(message, _undo is not null);
        var snapshot = _visibleSnapshot ?? _undo?.Snapshot;
        _overlay.PositionAt(
            snapshot?.Bounds ?? System.Windows.Rect.Empty,
            snapshot?.RightControlBounds,
            snapshot?.Host ?? ComposerHost.Codex);
    }

    private static bool RuntimeIdsEqual(IReadOnlyList<int> left, IReadOnlyList<int> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    internal static bool MatchesProviderText(string observed, string expected) =>
        string.Equals(observed, expected, StringComparison.Ordinal)
        || (observed.Length == expected.Length + 1
            && (observed[^1] == '\r' || observed[^1] == '\n')
            && observed.AsSpan(0, expected.Length).SequenceEqual(expected.AsSpan()));

    private void Localizer_OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_undo is not null)
        {
            _overlay.ShowStatus(FormatChangeCount(_undo.ChangeCount, _localizer), true);
        }
    }

    internal static string FormatChangeCount(int count, AppLocalizer localizer) =>
        count == 1
            ? localizer.Get(AppText.ChangeCountOne)
            : localizer.Get(AppText.ChangeCountMany, count);

    public void Dispose()
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _fallbackTimer.Stop();
        _focusWatcher.Dispose();
        _localizer.LanguageChanged -= Localizer_OnLanguageChanged;
        _overlay.Close();
    }

    private sealed record UndoState(ComposerSnapshot Snapshot, string Original, string Corrected, int ChangeCount);
}
