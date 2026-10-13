using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Threading;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.UI;
using CodexLanguageFix.Windows;
using WpfApplication = System.Windows.Application;

namespace CodexLanguageFix.SpeedProbe;

internal static class Program
{
    private const int HotkeyConflictExitCode = 20;
    private const int FocusGuardExitCode = 21;
    private const int ProbeFailureExitCode = 22;
    private const int MaximumInputToShortcutMs = 300;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [STAThread]
    private static int Main(string[] args)
    {
        ProbeArguments arguments;
        try
        {
            arguments = ProbeArguments.Parse(args);
        }
        catch (ProbeException exception)
        {
            Console.Error.WriteLine($"probe_failed={exception.Code}");
            return exception.ExitCode;
        }

        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            Console.Error.WriteLine("probe_failed=unsupported_runtime");
            return ProbeFailureExitCode;
        }

        var application = new WpfApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = ProbeFailureExitCode;
        application.Startup += async (_, _) =>
        {
            try
            {
                var sample = await new ProbeRunner(application.Dispatcher).RunAsync(arguments);
                Directory.CreateDirectory(Path.GetDirectoryName(arguments.ResultPath)!);
                await File.WriteAllTextAsync(arguments.ResultPath, JsonSerializer.Serialize(sample, JsonOptions));
                exitCode = 0;
            }
            catch (ProbeException exception)
            {
                Console.Error.WriteLine($"probe_failed={exception.Code}");
                exitCode = exception.ExitCode;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"probe_failed=unexpected_{exception.GetType().Name}");
                exitCode = ProbeFailureExitCode;
            }
            finally
            {
                application.Shutdown();
            }
        };

        _ = application.Run();
        return exitCode;
    }

    private sealed class ProbeRunner(Dispatcher dispatcher)
    {
        private readonly Dispatcher _dispatcher = dispatcher;
        private readonly CodexComposerAccessor _composer = new();
        private readonly AppLocalizer _localizer = new(AppLocalizer.German);
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(180));
        private string? _fixture;
        private ComposerSnapshot? _snapshot;
        private CorrectionCoordinator? _coordinator;
        private TrayController? _tray;
        private OverlayWindow? _overlay;
        private CodexAppServerClient? _client;
        private LunaCorrectionProvider? _provider;
        private DiagnosticLogger? _logger;
        private string? _dataDirectory;
        private string? _seededText;

        public async Task<ProbeSample> RunAsync(ProbeArguments arguments)
        {
            _dispatcher.VerifyAccess();
            _dataDirectory = Path.GetFullPath(arguments.DataDirectory);
            Directory.CreateDirectory(_dataDirectory);
            try
            {
                return await RunCoreAsync(arguments);
            }
            finally
            {
                await SafeRestoreFixtureAsync();
                DisposeProbeObjects();
                _lifetime.Dispose();
            }
        }

        private async Task<ProbeSample> RunCoreAsync(ProbeArguments arguments)
        {
            var preparationEnabled = arguments.Mode != ProbeMode.Manual;
            if (preparationEnabled && !string.Equals(arguments.Variant, "candidate", StringComparison.Ordinal))
                throw new ProbeException("preparation_requires_candidate_variant", ProbeFailureExitCode);

            var warmStarted = Stopwatch.GetTimestamp();
            _client = new CodexAppServerClient(_dataDirectory!, _localizer);
            _provider = new LunaCorrectionProvider(_client, _localizer, cacheEnabled: preparationEnabled);

            var account = await _client.GetAccountAsync(_lifetime.Token);
            if (!account.IsChatGpt)
                throw new ProbeException("chatgpt_account_unavailable", ProbeFailureExitCode);

            var selection = await _client.ResolveLunaProfileAsync(_lifetime.Token);
            await _provider.WarmUpAsync(_lifetime.Token);
            var warmUpMilliseconds = Stopwatch.GetElapsedTime(warmStarted).TotalMilliseconds;

            if (arguments.TargetWindow != 0) FocusExplicitComposer(arguments.TargetWindow);
            var body = BuildSyntheticText(arguments.Pair, arguments.LengthKind);
            var initial = CaptureKeyboardFocusedComposer();
            var emptyPlaceholder = false;
            if (initial is null && _composer.LastCaptureStatus == "ambiguous_placeholder")
            {
                initial = CaptureConfirmedEmptyPlaceholder();
                emptyPlaceholder = initial is not null;
            }
            if (initial is not null && IsKnownSyntheticFixture(initial.Text))
            {
                await ClearExactFixtureAsync(initial, initial.Text);
                initial = CaptureConfirmedEmptyPlaceholder() ?? CaptureKeyboardFocusedComposer();
                emptyPlaceholder = initial?.Text.TrimEnd('\r', '\n') == "Mit Codex arbeiten";
            }
            if (initial is null
                || initial.Host != ComposerHost.Codex
                || !string.Equals(initial.FieldCategory, "chat", StringComparison.Ordinal)
                || (!emptyPlaceholder && initial.Text.Length != 0)
                || initial.RuntimeId.Length == 0
                || initial.HostWindow == nint.Zero
                || initial.ProcessId == Environment.ProcessId
                || GetForegroundWindow() != initial.HostWindow)
            {
                Console.Error.WriteLine($"composer_guard: available={initial is not null}; host={initial?.Host}; category={initial?.FieldCategory}; empty={initial?.Text.Length == 0}; foregroundMatches={initial?.HostWindow == GetForegroundWindow()}; capture={_composer.LastCaptureStatus}");
                throw new ProbeException("focused_composer_must_be_empty_codex_chat", FocusGuardExitCode);
            }

            // Keep the precise editor object captured while it was keyboard-focused.
            // All later reads and writes are scoped to this object with SameEditor checks.
            _snapshot = initial;
            _seededText = body;
            _fixture = body + " ";
            EnsureNoForeignModifierHeld();
            var focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.GetRuntimeId().SequenceEqual(initial.RuntimeId)
                || !focused.Current.HasKeyboardFocus || GetForegroundWindow() != initial.HostWindow)
                throw new ProbeException("placeholder_focus_changed", FocusGuardExitCode);
            await SendUnicodeTextAsync(body, initial);

            var seedWait = Stopwatch.StartNew();
            var seeded = _composer.TryRefresh(initial);
            while (initial.SameEditor(seeded) && seeded!.Text != body && seedWait.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(20, _lifetime.Token);
                seeded = _composer.TryRefresh(initial);
            }
            if (!initial.SameEditor(seeded) || !string.Equals(seeded!.Text, body, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"seed_guard: observedLength={seeded?.Text.Length}; expectedLength={body.Length}; knownPrefix={seeded is not null && body.StartsWith(seeded.Text, StringComparison.Ordinal)}");
                throw new ProbeException("fixture_seed_verification_failed", ProbeFailureExitCode);
            }

            var settings = new AppSettings
            {
                Enabled = true,
                StartWithWindows = false,
                FirstRunNoticeShown = true,
                Language = AppLocalizer.German,
                CorrectionProvider = CorrectionProviderKind.Luna
            };
            if (preparationEnabled && !SetOptionalBooleanProperty(settings, "PrepareLunaCorrections", false))
                throw new ProbeException("preparation_setting_unavailable", ProbeFailureExitCode);
            if (!preparationEnabled)
                SetOptionalBooleanProperty(settings, "PrepareLunaCorrections", false);
            var settingsService = new SettingsService(Path.Combine(_dataDirectory!, "settings"));
            _tray = new TrayController(settings, settingsService, new AutostartService(_localizer), _localizer);
            _overlay = new OverlayWindow(_localizer);
            _logger = new DiagnosticLogger(_dataDirectory!);
            var logPath = Path.Combine(_dataDirectory!, "logs", $"diagnostic-{DateTime.Now:yyyy-MM-dd}.jsonl");
            var diagnosticsStartedAt = DateTimeOffset.Now;
            _coordinator = new CorrectionCoordinator(_composer,
                [_provider], _provider, _overlay, _tray, _logger, _dispatcher, _localizer);
            ((AutomationElement)initial.NativeElement).SetFocus();
            _coordinator.Start();
            if (LogHasEvent(Path.Combine(_dataDirectory!, "logs", $"diagnostic-{DateTime.Now:yyyy-MM-dd}.jsonl"), "shortcut_registration_failed"))
                throw new ProbeException("hotkey_registration_conflict", HotkeyConflictExitCode);
            var focusedProperty = typeof(CodexComposerAccessor).GetProperty(
                "LastCaptureWasKeyboardFocused", BindingFlags.Instance | BindingFlags.NonPublic);
            var hotkey = GetRequiredField(_coordinator, "_hotkey").GetValue(_coordinator);
            var registeredField = hotkey?.GetType().GetField("_registered", BindingFlags.Instance | BindingFlags.NonPublic);
            var activationWait = Stopwatch.StartNew();
            while ((focusedProperty?.GetValue(_composer) is not true || registeredField?.GetValue(hotkey) is not true)
                && activationWait.Elapsed < TimeSpan.FromSeconds(2))
            {
                ((AutomationElement)initial.NativeElement).SetFocus();
                await Task.Delay(50, _lifetime.Token);
            }
            if (focusedProperty?.GetValue(_composer) is not true || registeredField?.GetValue(hotkey) is not true)
                throw new ProbeException("coordinator_shortcut_not_active", HotkeyConflictExitCode);

            EnsureKeyboardAndForeground(initial, body);
            EnsureNoForeignModifierHeld();
            if (preparationEnabled) SetOptionalBooleanProperty(settings, "PrepareLunaCorrections", true);

            var initialFixtureInputTimestamp = Stopwatch.GetTimestamp();
            SendUnicodeSpace();
            var inputTimestamp = initialFixtureInputTimestamp;
            long hotkeyStarted;
            if (arguments.Mode == ProbeMode.Manual)
            {
                var read = typeof(CodexComposerAccessor).GetMethod("ReadText", BindingFlags.Static | BindingFlags.NonPublic,
                    [typeof(AutomationElement)])!;
                while (!string.Equals(read.Invoke(null, [initial.NativeElement]) as string, _fixture, StringComparison.Ordinal))
                {
                    if (Stopwatch.GetElapsedTime(inputTimestamp).TotalMilliseconds >= 250)
                        throw new ProbeException("last_input_not_verified_within_300ms", ProbeFailureExitCode);
                    Thread.Sleep(10);
                }
                hotkeyStarted = Stopwatch.GetTimestamp();
            }
            else
            {
                var waitAfterFinalSpace = arguments.Mode == ProbeMode.Prepare5s
                    ? TimeSpan.FromSeconds(5)
                    : TimeSpan.FromSeconds(2);
                await Task.Delay(waitAfterFinalSpace, _lifetime.Token);
                EnsureNoBackgroundWrite(initial, _fixture);
                await WaitForDiagnosticEventAsync(logPath, diagnosticsStartedAt, "preparation_started",
                    _fixture.Length, TimeSpan.FromSeconds(4), _lifetime.Token);

                if (arguments.Mode == ProbeMode.PrepareEdit)
                {
                    EnsureNoBackgroundWrite(initial, _fixture);
                    EnsureKeyboardFocusedFixture(initial, _fixture);
                    EnsureNoForeignModifierHeld();
                    const string suffix = "Weiter geht es.";
                    inputTimestamp = Stopwatch.GetTimestamp();
                    SendUnicodeText(suffix);
                    _fixture += suffix;
                    await WaitForFixtureAsync(initial, _fixture, TimeSpan.FromSeconds(3), _lifetime.Token);
                    EnsureNoBackgroundWrite(initial, _fixture);
                    EnsureKeyboardFocusedFixture(initial, _fixture);
                }

                EnsureNoBackgroundWrite(initial, _fixture);
                EnsureKeyboardFocusedFixture(initial, _fixture);
                EnsureNoForeignModifierHeld();
                hotkeyStarted = Stopwatch.GetTimestamp();
            }

            var inputToShortcutUpperBoundMs = Stopwatch.GetElapsedTime(inputTimestamp, hotkeyStarted).TotalMilliseconds;
            if (arguments.Mode == ProbeMode.Manual && inputToShortcutUpperBoundMs > MaximumInputToShortcutMs)
                throw new ProbeException("input_to_shortcut_exceeded_300ms", ProbeFailureExitCode);

            var correctionTimer = Stopwatch.StartNew();
            SendAltGrComma();
            var verifiedText = await WaitForVerifiedCoordinatorWriteAsync(initial, correctionTimer, _lifetime.Token);
            correctionTimer.Stop();

            if (!HasCoordinatorUndo(_coordinator))
                throw new ProbeException("coordinator_undo_not_available", ProbeFailureExitCode);
            if (!string.Equals(GetCoordinatorUndoOriginal(_coordinator), _fixture, StringComparison.Ordinal))
                throw new ProbeException("coordinator_captured_unexpected_fixture", ProbeFailureExitCode);

            InvokeCoordinatorUndo(_coordinator);
            var undone = _composer.TryRefresh(initial);
            if (!initial.SameEditor(undone)
                || !string.Equals(undone!.Text, _fixture, StringComparison.Ordinal)
                || !LogHasEvent(logPath, "composer_undo_completed"))
                throw new ProbeException("coordinator_undo_verification_failed", ProbeFailureExitCode);

            await ClearExactFixtureAsync(initial, _fixture);
            var cleared = _composer.TryRefresh(initial);
            if (!initial.SameEditor(cleared) || !IsEmptyComposer(cleared!))
                throw new ProbeException("fixture_cleanup_verification_failed", ProbeFailureExitCode);

            var diagnostics = ReadPreparationDiagnostics(
                logPath, diagnosticsStartedAt, _fixture.Length,
                arguments.Mode == ProbeMode.PrepareEdit ? _fixture.Length - "Weiter geht es.".Length : _fixture.Length);

            return new ProbeSample(
                arguments.Pair,
                arguments.Variant,
                Program.ModeName(arguments.Mode),
                arguments.LengthKind.ToString(),
                _fixture.Length,
                selection.Model,
                selection.Effort,
                selection.ServiceTier,
                CacheEnabled: preparationEnabled,
                PreparationStarted: diagnostics.PreparationStarted,
                PreparationCompleted: diagnostics.PreparationCompleted,
                PreparationCancelled: diagnostics.PreparationCancelled,
                PreparationAdopted: diagnostics.PreparationAdopted,
                CorrectionCacheHit: diagnostics.CorrectionCacheHit,
                PreparationElapsedMilliseconds: diagnostics.PreparationElapsedMilliseconds,
                ProviderElapsedMilliseconds: diagnostics.ProviderElapsedMilliseconds,
                WarmUpMilliseconds: warmUpMilliseconds,
                LastInputToShortcutUpperBoundMilliseconds: inputToShortcutUpperBoundMs,
                TriggerToVerifiedWriteMilliseconds: correctionTimer.Elapsed.TotalMilliseconds,
                CorrectedHash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verifiedText))),
                ExactExpected: verifiedText == _fixture.Replace("ein fehler", "ein Fehler", StringComparison.Ordinal),
                WriteVerified: true,
                UndoVerified: true,
                CleanupVerified: true);
        }

        private ComposerSnapshot? CaptureKeyboardFocusedComposer()
        {
            var method = typeof(CodexComposerAccessor).GetMethod(
                "TryCaptureKeyboardFocusedComposer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method is null)
                throw new ProbeException("baseline_keyboard_focus_capture_unavailable", ProbeFailureExitCode);
            return method.Invoke(_composer, null) as ComposerSnapshot;
        }

        private static ComposerSnapshot? CaptureConfirmedEmptyPlaceholder()
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.Current.HasKeyboardFocus || focused.Current.Name != "Mit Codex arbeiten")
                return null;
            var shape = typeof(CodexComposerAccessor).GetMethod("IsComposerShapeForHost",
                BindingFlags.Static | BindingFlags.NonPublic, [typeof(AutomationElement), typeof(ComposerHost)]);
            var host = typeof(CodexComposerAccessor).GetMethod("TryGetComposerHost",
                BindingFlags.Static | BindingFlags.NonPublic, [typeof(AutomationElement)]);
            if (shape?.Invoke(null, [focused, ComposerHost.Codex]) is not true
                || host?.Invoke(null, [focused]) is not ComposerHost.Codex) return null;
            var capture = typeof(CodexComposerAccessor).GetMethod("CreateSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object?[] parameters = capture.GetParameters().Length == 3
                ? [focused, ComposerHost.Codex, false] : [focused, ComposerHost.Codex, false, false];
            var snapshot = capture.Invoke(new CodexComposerAccessor(), parameters) as ComposerSnapshot;
            return snapshot?.Text.TrimEnd('\r', '\n') == focused.Current.Name ? snapshot : null;
        }

        private static void FocusExplicitComposer(long windowId)
        {
            if (GetForegroundWindow() != (nint)windowId)
                throw new ProbeException("selected_window_not_foreground", FocusGuardExitCode);
            var field = AutomationElement.FocusedElement;
            if (field is null || !field.Current.HasKeyboardFocus || field.Current.Name != "Mit Codex arbeiten")
                throw new ProbeException("selected_composer_not_focused", FocusGuardExitCode);
        }

        private static string BuildSyntheticText(int pair, ProbeLengthKind lengthKind)
        {
            var repetitions = lengthKind switch
            {
                ProbeLengthKind.Short => 0,
                ProbeLengthKind.Medium => 4,
                ProbeLengthKind.Long => 16,
                _ => throw new ArgumentOutOfRangeException(nameof(lengthKind))
            };
            var opening = $"Testlauf {pair + 1:00}: Das ist ein fehler.";
            const string sentence = " Der Text beschreibt einen einfachen Test. Zahlen, Reihenfolge und Aussage bleiben erhalten.";
            return opening + string.Concat(Enumerable.Repeat(sentence, repetitions));
        }

        private static bool IsKnownSyntheticFixture(string text)
        {
            for (var pair = 0; pair < 30; pair++)
            foreach (var length in Enum.GetValues<ProbeLengthKind>())
            {
                var fixture = BuildSyntheticText(pair, length);
                if (text == fixture || text == fixture + " ") return true;
            }
            return false;
        }

        private ComposerSnapshot VerifyFixtureAfterLastInput(ComposerSnapshot snapshot, string fixture, long inputTimestamp)
        {
            ComposerSnapshot? current = null;
            while (Stopwatch.GetElapsedTime(inputTimestamp) < TimeSpan.FromMilliseconds(MaximumInputToShortcutMs))
            {
                if (GetForegroundWindow() != snapshot.HostWindow)
                    throw new ProbeException("composer_lost_foreground", FocusGuardExitCode);
                current = _composer.TryRefresh(snapshot);
                if (snapshot.SameEditor(current) && string.Equals(current!.Text, fixture, StringComparison.Ordinal))
                    return current;
            }
            throw new ProbeException("last_input_not_verified_within_300ms", ProbeFailureExitCode);
        }

        private async Task<string> WaitForVerifiedCoordinatorWriteAsync(
            ComposerSnapshot snapshot,
            Stopwatch timer,
            CancellationToken cancellationToken)
        {
            while (timer.Elapsed < TimeSpan.FromSeconds(85))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var element = (AutomationElement)snapshot.NativeElement;
                if (!element.GetRuntimeId().SequenceEqual(snapshot.RuntimeId)
                    || GetForegroundWindow() != snapshot.HostWindow)
                    throw new ProbeException("measurement_editor_changed", FocusGuardExitCode);
                var read = typeof(CodexComposerAccessor).GetMethod("ReadText", BindingFlags.Static | BindingFlags.NonPublic,
                    [typeof(AutomationElement)])!;
                var currentText = read.Invoke(null, [element]) as string;
                if (!string.Equals(currentText, _fixture, StringComparison.Ordinal)
                    && HasCoordinatorUndo(_coordinator!)
                    && string.Equals(currentText, GetCoordinatorUndoCorrected(_coordinator!), StringComparison.Ordinal))
                {
                    return currentText!;
                }
                await Task.Delay(5, cancellationToken);
            }
            throw new ProbeException("correction_write_timeout", ProbeFailureExitCode);
        }

        private static bool LogHasEvent(string path, string eventName)
        {
            if (!File.Exists(path)) return false;
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    using var document = JsonDocument.Parse(line);
                    if (document.RootElement.TryGetProperty("eventName", out var value)
                        && string.Equals(value.GetString(), eventName, StringComparison.Ordinal))
                        return true;
                }
            }
            catch (IOException) { }
            catch (JsonException) { }
            return false;
        }

        private async Task WaitForDiagnosticEventAsync(
            string path,
            DateTimeOffset startedAt,
            string eventName,
            int characterCount,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReadDiagnosticEvents(path, startedAt).Any(entry =>
                        entry.EventName == eventName && entry.CharacterCount == characterCount))
                    return;
                await Task.Delay(50, cancellationToken);
            }
            throw new ProbeException("preparation_start_timeout", ProbeFailureExitCode);
        }

        private async Task WaitForFixtureAsync(
            ComposerSnapshot snapshot,
            string fixture,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (GetForegroundWindow() != snapshot.HostWindow)
                    throw new ProbeException("composer_lost_foreground", FocusGuardExitCode);
                var current = _composer.TryRefresh(snapshot);
                if (!snapshot.SameEditor(current))
                    throw new ProbeException("measurement_editor_changed", FocusGuardExitCode);
                if (string.Equals(current!.Text, fixture, StringComparison.Ordinal))
                    return;
                await Task.Delay(10, cancellationToken);
            }
            throw new ProbeException("fixture_edit_verification_failed", ProbeFailureExitCode);
        }

        private void EnsureNoBackgroundWrite(ComposerSnapshot snapshot, string fixture)
        {
            if (_coordinator is null || HasCoordinatorUndo(_coordinator))
                throw new ProbeException("background_write_before_explicit_shortcut", ProbeFailureExitCode);
            EnsureKeyboardAndForeground(snapshot, fixture);
        }

        private void EnsureKeyboardFocusedFixture(ComposerSnapshot snapshot, string fixture)
        {
            EnsureKeyboardAndForeground(snapshot, fixture);
            var focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.Current.HasKeyboardFocus
                || !focused.GetRuntimeId().SequenceEqual(snapshot.RuntimeId))
                throw new ProbeException("focused_composer_changed_before_input", FocusGuardExitCode);
        }

        private static PreparationDiagnostics ReadPreparationDiagnostics(
            string path,
            DateTimeOffset startedAt,
            int finalCharacterCount,
            int originalCharacterCount)
        {
            var events = ReadDiagnosticEvents(path, startedAt)
                .Where(entry => entry.CharacterCount == finalCharacterCount
                    || entry.CharacterCount == originalCharacterCount)
                .ToArray();
            var provider = events.LastOrDefault(entry =>
                entry.EventName == "correction_provider_completed"
                && entry.CharacterCount == finalCharacterCount);
            var completed = events.LastOrDefault(entry =>
                entry.EventName == "preparation_completed"
                && entry.CharacterCount == finalCharacterCount);

            return new PreparationDiagnostics(
                PreparationStarted: events.Any(entry => entry.EventName == "preparation_started"),
                PreparationCompleted: completed is not null,
                PreparationCancelled: events.Any(entry => entry.EventName == "preparation_cancelled"),
                PreparationAdopted: events.Any(entry => entry.EventName == "preparation_adopted"
                    && entry.CharacterCount == finalCharacterCount),
                CorrectionCacheHit: string.Equals(provider?.Reason, "cache_hit", StringComparison.Ordinal),
                PreparationElapsedMilliseconds: completed?.ElapsedMilliseconds,
                ProviderElapsedMilliseconds: provider?.ElapsedMilliseconds);
        }

        private static IEnumerable<ProbeDiagnosticEvent> ReadDiagnosticEvents(string path, DateTimeOffset startedAt)
        {
            if (!File.Exists(path)) yield break;
            foreach (var line in File.ReadLines(path))
            {
                JsonDocument? document = null;
                ProbeDiagnosticEvent? parsed = null;
                try
                {
                    document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("timestamp", out var timestampValue)
                        || !DateTimeOffset.TryParse(timestampValue.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var timestamp)
                        || timestamp < startedAt
                        || !root.TryGetProperty("eventName", out var eventValue)
                        || !root.TryGetProperty("characterCount", out var countValue)
                        || countValue.ValueKind != JsonValueKind.Number)
                        continue;

                    var elapsed = root.TryGetProperty("elapsedMilliseconds", out var elapsedValue)
                        && elapsedValue.ValueKind == JsonValueKind.Number
                        ? elapsedValue.GetDouble() : (double?)null;
                    var reason = root.TryGetProperty("reason", out var reasonValue)
                        && reasonValue.ValueKind == JsonValueKind.String
                        ? reasonValue.GetString() : null;
                    parsed = new ProbeDiagnosticEvent(
                        eventValue.GetString() ?? string.Empty,
                        countValue.GetInt32(),
                        elapsed,
                        reason);
                }
                catch (IOException) { }
                catch (JsonException) { }
                finally { document?.Dispose(); }
                if (parsed is not null) yield return parsed;
            }
        }

        private static bool HasCoordinatorUndo(CorrectionCoordinator coordinator) =>
            GetRequiredField(coordinator, "_undo").GetValue(coordinator) is not null;

        private static string? GetCoordinatorUndoOriginal(CorrectionCoordinator coordinator)
        {
            var state = GetRequiredField(coordinator, "_undo").GetValue(coordinator);
            return state?.GetType().GetProperty("Original", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state) as string;
        }

        private static string? GetCoordinatorUndoCorrected(CorrectionCoordinator coordinator)
        {
            var state = GetRequiredField(coordinator, "_undo").GetValue(coordinator);
            return state?.GetType().GetProperty("Corrected", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state) as string;
        }

        private static void InvokeCoordinatorUndo(CorrectionCoordinator coordinator)
        {
            var method = coordinator.GetType().GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new ProbeException("coordinator_undo_unavailable", ProbeFailureExitCode);
            method.Invoke(coordinator, null);
        }

        private static FieldInfo GetRequiredField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new ProbeException("baseline_coordinator_shape_unavailable", ProbeFailureExitCode);

        private static bool SetOptionalBooleanProperty(object target, string propertyName, bool value)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.CanWrite == true && property.PropertyType == typeof(bool))
            {
                property.SetValue(target, value);
                return true;
            }
            return false;
        }

        private static void EnsureNoForeignModifierHeld()
        {
            foreach (var key in new ushort[] { 0x11, 0x12, 0xA2, 0xA3, 0xA4, 0xA5, 0xBC })
            {
                if ((GetAsyncKeyState(key) & 0x8000) != 0)
                    throw new ProbeException("modifier_or_comma_key_already_held", FocusGuardExitCode);
            }

            var language = unchecked((ushort)(GetKeyboardLayout(0).ToInt64() & 0xFFFF));
            if ((language & 0x03FF) != 0x0007)
                throw new ProbeException("german_keyboard_layout_required", FocusGuardExitCode);
        }

        private void EnsureKeyboardAndForeground(ComposerSnapshot snapshot, string fixture)
        {
            var current = _composer.TryRefresh(snapshot);
            if (!snapshot.SameEditor(current)
                || !string.Equals(current!.Text, fixture, StringComparison.Ordinal)
                || GetForegroundWindow() != snapshot.HostWindow)
            {
                throw new ProbeException("focused_composer_changed_before_input", FocusGuardExitCode);
            }
        }

        private static void SendUnicodeSpace()
        {
            var inputs = new[]
            {
                KeyInput(0, 0x0020, 0x0004),
                KeyInput(0, 0x0020, 0x0004 | 0x0002)
            };
            SendInputExactly(inputs, "synthetic_space_input_failed");
        }

        private static void SendUnicodeText(string text)
        {
            var inputs = text.SelectMany(character => new[]
            {
                KeyInput(0, character, 0x0004), KeyInput(0, character, 0x0004 | 0x0002)
            }).ToArray();
            SendInputExactly(inputs, "synthetic_fixture_input_failed");
        }

        private static async Task SendUnicodeTextAsync(string text, ComposerSnapshot snapshot)
        {
            for (var offset = 0; offset < text.Length; offset += 64)
            {
                var focused = AutomationElement.FocusedElement;
                if (focused is null || !focused.GetRuntimeId().SequenceEqual(snapshot.RuntimeId)
                    || GetForegroundWindow() != snapshot.HostWindow)
                    throw new ProbeException("synthetic_input_focus_changed", FocusGuardExitCode);
                SendUnicodeText(text.Substring(offset, Math.Min(64, text.Length - offset)));
                await Task.Delay(5);
            }
        }

        private static void SendAltGrComma()
        {
            var inputs = new[]
            {
                KeyInput(0xA5, 0, 0x0001),
                KeyInput(0xBC, 0, 0),
                KeyInput(0xBC, 0, 0x0002),
                KeyInput(0xA5, 0, 0x0001 | 0x0002)
            };
            SendInputExactly(inputs, "altgr_hotkey_input_failed");
        }

        private static void SendInputExactly(NativeInput[] inputs, string failureCode)
        {
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
            if (sent != (uint)inputs.Length)
            {
                ReleaseInjectedKeys();
                throw new ProbeException(failureCode, ProbeFailureExitCode);
            }
        }

        private static void ReleaseInjectedKeys()
        {
            NativeInput[] releases =
            [
                KeyInput(0xA5, 0, 0x0001 | 0x0002),
                KeyInput(0xBC, 0, 0x0002)
            ];
            _ = SendInput((uint)releases.Length, releases, Marshal.SizeOf<NativeInput>());
        }

        private static NativeInput KeyInput(ushort virtualKey, ushort scanCode, uint flags) => new()
        {
            Type = 1,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags
                }
            }
        };

        private static bool IsEmptyComposer(ComposerSnapshot snapshot) => snapshot.Text.Length == 0
            || snapshot.Text.TrimEnd('\r', '\n') == "Mit Codex arbeiten";

        private async Task ClearExactFixtureAsync(ComposerSnapshot snapshot, string fixture)
        {
            var current = _composer.TryRefresh(snapshot);
            if (!snapshot.SameEditor(current) || !string.Equals(current!.Text, fixture, StringComparison.Ordinal))
                throw new ProbeException("fixture_cleanup_guard_rejected", ProbeFailureExitCode);
            EnsureKeyboardAndForeground(snapshot, fixture);
            var focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.GetRuntimeId().SequenceEqual(snapshot.RuntimeId))
                throw new ProbeException("fixture_cleanup_focus_changed", FocusGuardExitCode);
            EnsureNoForeignModifierHeld();
            SendInputExactly([
                KeyInput(0xA2, 0, 0), KeyInput(0x41, 0, 0), KeyInput(0x41, 0, 0x0002), KeyInput(0xA2, 0, 0x0002),
                KeyInput(0x08, 0, 0), KeyInput(0x08, 0, 0x0002)
            ], "fixture_cleanup_input_failed");
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(2))
            {
                current = _composer.TryRefresh(snapshot);
                if (!snapshot.SameEditor(current)) throw new ProbeException("fixture_cleanup_editor_changed", FocusGuardExitCode);
                if (IsEmptyComposer(current!)) return;
                await Task.Delay(20);
            }
            throw new ProbeException("fixture_cleanup_readback_failed", ProbeFailureExitCode);
        }

        private async Task SafeRestoreFixtureAsync()
        {
            if (_snapshot is null || _fixture is null) return;
            try
            {
                if (_coordinator is not null)
                {
                    if (HasCoordinatorUndo(_coordinator))
                    {
                        InvokeCoordinatorUndo(_coordinator);
                        await WaitForCoordinatorIdleAsync(_coordinator);
                    }
                    else
                    {
                        _coordinator.Dispose();
                        await WaitForCoordinatorIdleAsync(_coordinator);
                    }
                }

                var current = _composer.TryRefresh(_snapshot);
                if (!_snapshot.SameEditor(current)) return;

                if (string.Equals(current!.Text, _fixture, StringComparison.Ordinal)
                    || string.Equals(current.Text, _seededText, StringComparison.Ordinal))
                {
                    await ClearExactFixtureAsync(current, current.Text);
                    return;
                }
            }
            catch
            {
                // Do not attempt a broader clear if the exact synthetic fixture cannot be reverified.
            }
        }

        private async Task WaitForCoordinatorIdleAsync(CorrectionCoordinator coordinator)
        {
            var busyField = coordinator.GetType().GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic);
            if (busyField is null) return;
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(15)
                && busyField.GetValue(coordinator) is true)
            {
                await Task.Delay(10);
            }
        }

        private void DisposeProbeObjects()
        {
            try
            {
                if (_coordinator is not null)
                    _coordinator.Dispose();
            }
            catch { }

            try { _overlay?.Close(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(ushort virtualKey);

        [DllImport("user32.dll")]
        private static extern nint GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint window);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint Type;
            public InputUnion Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(24)] public long NativeSizePadding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public nint ExtraInfo;
        }
    }

    private sealed record ProbeArguments(
        int Pair,
        string Variant,
        ProbeLengthKind LengthKind,
        string ResultPath,
        string DataDirectory,
        long TargetWindow,
        ProbeMode Mode)
    {
        public static ProbeArguments Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ProbeException("invalid_arguments", ProbeFailureExitCode);
                values[args[i][2..]] = args[i + 1];
            }

            if (!int.TryParse(values.GetValueOrDefault("pair"), NumberStyles.None, CultureInfo.InvariantCulture, out var pair)
                || pair is < 0 or > 29
                || !values.TryGetValue("variant", out var variant)
                || variant is not ("baseline" or "candidate")
                || !values.TryGetValue("length", out var length)
                || !Enum.TryParse<ProbeLengthKind>(length, true, out var lengthKind)
                || !values.TryGetValue("result", out var result)
                || !values.TryGetValue("data-directory", out var dataDirectory))
                throw new ProbeException("invalid_arguments", ProbeFailureExitCode);

            var mode = values.TryGetValue("mode", out var modeText)
                ? modeText.ToLowerInvariant() switch
                {
                    "manual" => ProbeMode.Manual,
                    "prepare-2s" => ProbeMode.Prepare2s,
                    "prepare-5s" => ProbeMode.Prepare5s,
                    "prepare-edit" => ProbeMode.PrepareEdit,
                    _ => throw new ProbeException("invalid_arguments", ProbeFailureExitCode)
                }
                : ProbeMode.Manual;

            var targetWindow = values.TryGetValue("target-window", out var target)
                ? long.Parse(target, CultureInfo.InvariantCulture) : 0;
            return new ProbeArguments(pair, variant, lengthKind, Path.GetFullPath(result), Path.GetFullPath(dataDirectory), targetWindow, mode);
        }
    }

    private sealed record ProbeSample(
        int Pair,
        string Variant,
        string Mode,
        string LengthKind,
        int CharacterCount,
        string Model,
        string Effort,
        string ServiceTier,
        bool CacheEnabled,
        bool PreparationStarted,
        bool PreparationCompleted,
        bool PreparationCancelled,
        bool PreparationAdopted,
        bool CorrectionCacheHit,
        double? PreparationElapsedMilliseconds,
        double? ProviderElapsedMilliseconds,
        double WarmUpMilliseconds,
        double LastInputToShortcutUpperBoundMilliseconds,
        double TriggerToVerifiedWriteMilliseconds,
        string CorrectedHash,
        bool ExactExpected,
        bool WriteVerified,
        bool UndoVerified,
        bool CleanupVerified);

    private sealed record PreparationDiagnostics(
        bool PreparationStarted,
        bool PreparationCompleted,
        bool PreparationCancelled,
        bool PreparationAdopted,
        bool CorrectionCacheHit,
        double? PreparationElapsedMilliseconds,
        double? ProviderElapsedMilliseconds);

    private sealed record ProbeDiagnosticEvent(
        string EventName,
        int CharacterCount,
        double? ElapsedMilliseconds,
        string? Reason);

    private enum ProbeLengthKind { Short, Medium, Long }
    private enum ProbeMode { Manual, Prepare2s, Prepare5s, PrepareEdit }

    private static string ModeName(ProbeMode mode) => mode switch
    {
        ProbeMode.Manual => "manual",
        ProbeMode.Prepare2s => "prepare-2s",
        ProbeMode.Prepare5s => "prepare-5s",
        ProbeMode.PrepareEdit => "prepare-edit",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private sealed class ProbeException(string code, int exitCode) : Exception
    {
        public string Code { get; } = code;
        public int ExitCode { get; } = exitCode;
    }
}
