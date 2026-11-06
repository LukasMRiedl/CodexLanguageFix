using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.UI;

namespace CodexLanguageFix.Tests;

[Collection("Correction hotkey")]
public sealed class CoordinatorPreparationTests
{
    private static readonly TimeSpan DispatcherTimeout = TimeSpan.FromSeconds(4);

    [Fact]
    public void MissingEditorRecoversThroughPollingAndSettingsPauseStopsRecovery()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var timer = (DispatcherTimer)typeof(CorrectionCoordinator)
                .GetField("_fallbackTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(scenario.Coordinator)!;
            var visible = typeof(CorrectionCoordinator)
                .GetField("_visibleSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var overlay = (OverlayWindow)typeof(CorrectionCoordinator)
                .GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(scenario.Coordinator)!;
            var refreshing = typeof(CorrectionCoordinator)
                .GetField("_refreshing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hotkey = (CodexLanguageFix.Windows.CorrectionHotkey)typeof(CorrectionCoordinator)
                .GetField("_hotkey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(scenario.Coordinator)!;
            var registered = typeof(CodexLanguageFix.Windows.CorrectionHotkey)
                .GetField("_registered", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.True(hotkey.SetShortcut(new CorrectionShortcut(0, 0x85)));
            Assert.True(hotkey.SetEnabled(true));

            scenario.Coordinator.Start();
            Assert.True(PumpUntil(dispatcher, () => !(bool)refreshing.GetValue(scenario.Coordinator)!, DispatcherTimeout));
            Assert.True(timer.IsEnabled);
            Assert.Null(visible.GetValue(scenario.Coordinator));
            Assert.False((bool)registered.GetValue(hotkey)!);
            Assert.False(overlay.IsVisible);

            var recovered = Snapshot("Wieder verfügbares Feld");
            scenario.Composer.Current = recovered;
            Assert.True(PumpUntil(dispatcher, () => ReferenceEquals(recovered, visible.GetValue(scenario.Coordinator)), DispatcherTimeout),
                "Ein wieder verfügbares Feld muss ohne neues WinEvent durch den Timer erkannt werden.");
            Assert.Empty(scenario.Composer.Writes);

            typeof(CorrectionCoordinator).GetField("_settingsOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(scenario.Coordinator, true);
            InvokeCoordinatorMethod(scenario.Coordinator, "RefreshOverlay");
            Assert.True(PumpUntil(dispatcher, () => !(bool)refreshing.GetValue(scenario.Coordinator)!, DispatcherTimeout));
            Assert.False(timer.IsEnabled);
            Assert.Null(visible.GetValue(scenario.Coordinator));
            Assert.False((bool)registered.GetValue(hotkey)!);
            Assert.False(overlay.IsVisible);
            Assert.Empty(scenario.Composer.Writes);
        });
    }

    [Fact]
    public void OpenSettings_PreventsCorrectionFromRunning()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            scenario.Composer.Current = Snapshot("Das ist ein fehler.");
            typeof(CorrectionCoordinator).GetField("_settingsOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(scenario.Coordinator, true);
            var correction = InvokeManualCorrection(scenario.Coordinator);
            Assert.True(PumpUntil(dispatcher, () => correction.IsCompleted, DispatcherTimeout));
            correction.GetAwaiter().GetResult();
            Assert.Equal(0, scenario.Luna.RequestCount);
            Assert.Empty(scenario.Composer.Writes);
        });
    }


    [Fact]
    public void SettingsRecording_PersistsNewShortcutThenCorrectsAndUndoesThroughTheMessageHandler()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var snapshot = Snapshot("text mit fehler");
            scenario.Composer.Current = snapshot;
            Exception? dialogFailure = null;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var window = (SettingsWindow)typeof(CorrectionCoordinator)
                    .GetField("_settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(scenario.Coordinator)!;
                try
                {
                    Assert.True((bool)typeof(CorrectionCoordinator)
                        .GetField("_settingsOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(scenario.Coordinator)!);
                    var source = System.Windows.PresentationSource.FromVisual(window)!;
                    ((System.Windows.Controls.Button)window.FindName("RecordButton")!)
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Assert.False(((System.Windows.Controls.Button)window.FindName("SaveButton")!).IsEnabled);
                    window.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount,
                        System.Windows.Input.Key.F23)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                    Assert.Equal("F23", ((System.Windows.Controls.TextBox)window.FindName("ShortcutDisplayTextBox")!).Text);
                    ((System.Windows.Controls.Button)window.FindName("SaveButton")!)
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Assert.True(window.DialogResult);
                }
                catch (Exception exception) { dialogFailure = exception; window.Close(); }
            }));
            InvokeCoordinatorMethod(scenario.Coordinator, "Tray_OnSettingsRequested", null, EventArgs.Empty);
            if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
            Assert.False((bool)typeof(CorrectionCoordinator)
                .GetField("_settingsOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(scenario.Coordinator)!);
            var reloaded = new SettingsService(scenario.DirectoryPath).Load();
            var savedShortcut = new CorrectionShortcut(reloaded.ShortcutModifiers, reloaded.ShortcutVirtualKey);
            Assert.Equal(new CorrectionShortcut(0, 0x86), savedShortcut);
            var hotkey = (CodexLanguageFix.Windows.CorrectionHotkey)typeof(CorrectionCoordinator)
                .GetField("_hotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Coordinator)!;
            Assert.Equal(savedShortcut, hotkey.Shortcut);
            scenario.Luna.CompleteImmediately(snapshot.Text);
            Assert.True(hotkey.SetEnabled(true));
            var handled = false;
            var messageArgs = new object[] { nint.Zero, 0x0312, (nint)1,
                (nint)((savedShortcut.VirtualKey << 16) | savedShortcut.Modifiers), handled };
            typeof(CodexLanguageFix.Windows.CorrectionHotkey)
                .GetMethod("OnMessage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(hotkey, messageArgs);
            Assert.True((bool)messageArgs[4]);
            Assert.True(PumpUntil(dispatcher, () => scenario.Composer.Writes.Count == 1 && !(bool)typeof(CorrectionCoordinator)
                .GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Coordinator)!, DispatcherTimeout));
            Assert.Equal(Corrected(snapshot.Text), scenario.Composer.Current!.Text);
            InvokeCoordinatorMethod(scenario.Coordinator, "Undo");
            Assert.Equal(snapshot.Text, scenario.Composer.Current!.Text);
            Assert.Contains("undone", scenario.LastStatus, StringComparison.OrdinalIgnoreCase);
            using var restartedHotkey = new CodexLanguageFix.Windows.CorrectionHotkey();
            Assert.True(restartedHotkey.SetShortcut(savedShortcut));
            Assert.Equal(savedShortcut, restartedHotkey.Shortcut);
        });
    }

    [Fact]
    public void ManualRequestAdoptsRunningPreparationAndWritesOnlyAfterTheClick()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var snapshot = Snapshot("text mit fehler");
            scenario.Composer.Current = snapshot;

            var preparationTask = StartPreparation(scenario.Coordinator, snapshot);
            var pending = scenario.Luna.GetPending(snapshot.Text);
            Assert.Equal(1, scenario.Luna.InferenceCount);
            Assert.Empty(scenario.Composer.Writes);

            var manualTask = InvokeManualCorrection(scenario.Coordinator);
            Assert.False(manualTask.IsCompleted);
            Assert.Empty(scenario.Composer.Writes);

            scenario.Luna.Complete(snapshot.Text);
            Assert.True(PumpUntil(dispatcher, () => manualTask.IsCompleted, DispatcherTimeout),
                "Die manuelle Korrektur hat die passende laufende Luna-Vorbereitung nicht übernommen.");
            manualTask.GetAwaiter().GetResult();

            Assert.False(pending.Token.IsCancellationRequested);
            Assert.Equal(2, scenario.Luna.RequestCount);
            Assert.Equal(1, scenario.Luna.InferenceCount);
            Assert.Single(scenario.Composer.Writes);
            Assert.Contains("Prompt corrected", scenario.LastStatus);
            Assert.Equal(snapshot.Text, scenario.Composer.Writes[0].ExpectedText);
            Assert.Equal(Corrected(snapshot.Text), scenario.Composer.Writes[0].Replacement);
            Assert.True(PumpUntil(dispatcher, () => preparationTask.IsCompleted, DispatcherTimeout));
            Assert.True(preparationTask.GetAwaiter().GetResult());
        });
    }

    [Fact]
    public void CompletedPreparationDoesNotWriteWithoutAnExplicitManualRequest()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var snapshot = Snapshot("ohne klick");
            scenario.Composer.Current = snapshot;

            var preparationTask = StartPreparation(scenario.Coordinator, snapshot);
            scenario.Luna.Complete(snapshot.Text);

            Assert.True(PumpUntil(dispatcher, () => preparationTask.IsCompleted, DispatcherTimeout));
            Assert.True(preparationTask.GetAwaiter().GetResult());
            Assert.True(scenario.Preparation.IsReady(snapshot));
            Assert.Empty(scenario.Composer.Writes);
            Assert.Equal(1, scenario.Luna.InferenceCount);
        });
    }

    [Fact]
    public void TextChangedAfterPreparationStartsCannotWriteTheStaleResult()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var original = Snapshot("alter text");
            scenario.Composer.Current = original;

            var preparationTask = StartPreparation(scenario.Coordinator, original);
            var pending = scenario.Luna.GetPending(original.Text);
            var updated = original with { Text = "neuer text" };
            scenario.Composer.Current = updated;
            scenario.Preparation.Observe(updated, eligible: true, allowStart: false);

            Assert.True(pending.Token.IsCancellationRequested);
            scenario.Luna.Complete(original.Text);

            Assert.True(PumpUntil(dispatcher, () => preparationTask.IsCompleted, DispatcherTimeout));
            Assert.False(preparationTask.GetAwaiter().GetResult());
            Assert.False(scenario.Preparation.IsReady(original));
            Assert.Empty(scenario.Composer.Writes);
        });
    }

    [Fact]
    public void StalePreparationDoesNotDelayManualCorrectionOfNewText()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var original = Snapshot("alter text");
            scenario.Composer.Current = original;
            var preparationTask = StartPreparation(scenario.Coordinator, original);
            var stale = scenario.Luna.GetPending(original.Text);

            var updated = original with { Text = "neuer text" };
            scenario.Composer.Current = updated;
            scenario.Luna.CompleteImmediately(updated.Text);

            var manualTask = InvokeManualCorrection(scenario.Coordinator);
            var completedWithoutStale = PumpUntil(dispatcher, () => manualTask.IsCompleted, DispatcherTimeout);
            if (!completedWithoutStale)
            {
                scenario.Luna.Complete(original.Text);
                _ = PumpUntil(dispatcher, () => manualTask.IsCompleted, DispatcherTimeout);
            }

            Assert.True(completedWithoutStale,
                "Die Korrektur des neuen Texts wartete auf die veraltete Luna-Vorbereitung.");
            manualTask.GetAwaiter().GetResult();
            Assert.True(stale.Token.IsCancellationRequested);
            Assert.False(stale.Completion.Task.IsCompleted);
            Assert.Single(scenario.Composer.Writes);
            Assert.Equal(updated.Text, scenario.Composer.Writes[0].ExpectedText);
            Assert.Equal(Corrected(updated.Text), scenario.Composer.Writes[0].Replacement);

            scenario.Luna.Complete(original.Text);
            Assert.True(PumpUntil(dispatcher, () => preparationTask.IsCompleted, DispatcherTimeout));
            Assert.False(preparationTask.GetAwaiter().GetResult());
            Assert.Single(scenario.Composer.Writes);
        });
    }

    [Fact]
    public void UnfocusedComposerDoesNotStartBackgroundProviderRequests()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var snapshot = Snapshot("keine automatische Anfrage");
            snapshot = snapshot with { Bounds = Rect.Empty };
            scenario.Composer.Current = snapshot;

            InvokeCoordinatorMethod(scenario.Coordinator, "RefreshOverlay");
            PumpFor(dispatcher, CorrectionPreparation.QuietPeriod + TimeSpan.FromMilliseconds(100));

            Assert.False(scenario.Preparation.IsReady(snapshot));
            Assert.Equal(0, scenario.Luna.RequestCount);
            Assert.Empty(scenario.Composer.Writes);
        });
    }

    [Fact]
    public void ProviderFailureDoesNotWriteAndReportsUnavailableStatus()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            var snapshot = Snapshot("Text mit Fehler");
            scenario.Composer.Current = snapshot;
            scenario.Luna.FailImmediately(snapshot.Text);

            var correction = InvokeManualCorrection(scenario.Coordinator);
            Assert.True(PumpUntil(dispatcher, () => correction.IsCompleted, DispatcherTimeout));
            correction.GetAwaiter().GetResult();

            Assert.Equal(snapshot.Text, scenario.Composer.Current!.Text);
            Assert.Empty(scenario.Composer.Writes);
            Assert.Contains("Correction unavailable", scenario.LastStatus);
        });
    }

    [Fact]
    public void GeometryRefreshKeepsVisibleButtonUntilValidatedReplacementButFocusLossHidesIt()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            using var release = new ManualResetEventSlim();
            scenario.Composer.OnPoll = () => release.Wait();
            var overlay = (OverlayWindow)typeof(CorrectionCoordinator)
                .GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Coordinator)!;
            overlay.Show();
            try
            {
                InvokeCoordinatorMethod(scenario.Coordinator, "FocusWatcher_OnChanged", null,
                    new CodexLanguageFix.Windows.ComposerChangeEventArgs(false));
                Assert.True(overlay.IsVisible);
                InvokeCoordinatorMethod(scenario.Coordinator, "FocusWatcher_OnChanged", null,
                    new CodexLanguageFix.Windows.ComposerChangeEventArgs(true));
                Assert.False(overlay.IsVisible);
            }
            finally { release.Set(); }
            Assert.True(PumpUntil(dispatcher, () => !(bool)typeof(CorrectionCoordinator)
                .GetField("_refreshing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Coordinator)!, DispatcherTimeout));
        });
    }

    [Fact]
    public void GeometryUpdateKeepsShortcutRegisteredWhileCaptureIsPending()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            using var release = new ManualResetEventSlim();
            scenario.Composer.OnPoll = () => release.Wait();
            var hotkey = (CodexLanguageFix.Windows.CorrectionHotkey)typeof(CorrectionCoordinator)
                .GetField("_hotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Coordinator)!;
            Assert.True(hotkey.SetShortcut(new CorrectionShortcut(0, 0x85)));
            Assert.True(hotkey.SetEnabled(true));
            try
            {
                InvokeCoordinatorMethod(scenario.Coordinator, "FocusWatcher_OnChanged", null,
                    new CodexLanguageFix.Windows.ComposerChangeEventArgs(false));
                Assert.Equal(0L, typeof(CorrectionCoordinator).GetField("_focusGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Coordinator));
                Assert.True((bool)typeof(CodexLanguageFix.Windows.CorrectionHotkey).GetField("_registered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hotkey)!);
                InvokeCoordinatorMethod(scenario.Coordinator, "FocusWatcher_OnChanged", null,
                    new CodexLanguageFix.Windows.ComposerChangeEventArgs(true));
                Assert.False((bool)typeof(CodexLanguageFix.Windows.CorrectionHotkey).GetField("_registered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hotkey)!);
            }
            finally { release.Set(); }
            Assert.True(PumpUntil(dispatcher, () => !(bool)typeof(CorrectionCoordinator)
                .GetField("_refreshing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Coordinator)!, DispatcherTimeout));
        });
    }

    [Fact]
    public void FocusChangeDuringCaptureRejectsStaleResultBeforeProviderRequest()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            scenario.Composer.Current = Snapshot("Original");
            scenario.Composer.OnCapture = () => { entered.Set(); release.Wait(); };
            var correction = InvokeManualCorrection(scenario.Coordinator);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
                InvokeCoordinatorMethod(scenario.Coordinator, "FocusWatcher_OnChanged", null, EventArgs.Empty);
            }
            finally { release.Set(); }
            Assert.True(PumpUntil(dispatcher, () => correction.IsCompleted, DispatcherTimeout));
            correction.GetAwaiter().GetResult();
            Assert.Equal(0, scenario.Luna.RequestCount);
            Assert.Empty(scenario.Composer.Writes);
        });
    }

    [Fact]
    public void RepeatedClickWhileCaptureIsPendingDoesNotQueueAnotherCorrection()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            scenario.Composer.Current = Snapshot("Original");
            scenario.Luna.CompleteImmediately("Original");
            scenario.Composer.OnCapture = () => { entered.Set(); release.Wait(); };
            var first = InvokeManualCorrection(scenario.Coordinator);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
                Assert.True(InvokeManualCorrection(scenario.Coordinator).IsCompletedSuccessfully);
            }
            finally { release.Set(); }
            Assert.True(PumpUntil(dispatcher, () => first.IsCompleted, DispatcherTimeout));
            first.GetAwaiter().GetResult();
            Assert.Equal(1, scenario.Luna.RequestCount);
            Assert.Single(scenario.Composer.Writes);
        });
    }

    [Fact]
    public async Task CodexOnlyLiveCorrectionAndUndoUseSyntheticComposer()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_CODEX_ONLY_SMOKE") != "1")
            return;

        var localizer = new AppLocalizer("de");
        using var client = new CodexAppServerClient(LunaLiveRuntime.CreateDirectory(), localizer);
        var account = await client.GetAccountAsync(CancellationToken.None);
        Assert.True(account.IsChatGpt);
        var profile = await client.ResolveLunaProfileAsync(CancellationToken.None);
        Assert.Equal("low", profile.Effort);
        Assert.Equal("priority", profile.ServiceTier);

        var provider = new LunaCorrectionProvider(client, localizer);
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario(provider, provider);
            const string original = "Das ist ein test.";
            scenario.Composer.Current = Snapshot(original);

            var click = InvokeManualCorrection(scenario.Coordinator);
            Assert.True(PumpUntil(dispatcher, () => click.IsCompleted, TimeSpan.FromSeconds(90)));
            click.GetAwaiter().GetResult();
            Assert.Single(scenario.Composer.Writes);
            Assert.Contains("Test", scenario.Composer.Current!.Text, StringComparison.Ordinal);
            Assert.NotEqual(original, scenario.Composer.Current.Text);
            InvokeCoordinatorMethod(scenario.Coordinator, "Undo");
            Assert.Equal(original, scenario.Composer.Current.Text);
            Assert.Equal(2, scenario.Composer.Writes.Count);

            var shortcut = (Task)typeof(CorrectionCoordinator)
                .GetMethod("CorrectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(scenario.Coordinator, [true])!;
            Assert.True(PumpUntil(dispatcher, () => shortcut.IsCompleted, TimeSpan.FromSeconds(90)));
            shortcut.GetAwaiter().GetResult();
            Assert.Equal(3, scenario.Composer.Writes.Count);
            Assert.Contains("Test", scenario.Composer.Current!.Text, StringComparison.Ordinal);
            InvokeCoordinatorMethod(scenario.Coordinator, "Undo");
            Assert.Equal(original, scenario.Composer.Current.Text);
            Assert.Equal(4, scenario.Composer.Writes.Count);
        }, TimeSpan.FromSeconds(210));
    }

    private static ComposerSnapshot Snapshot(string text) => new(
        new object(), [1], text, new Rect(0, 0, 100, 40), 123);

    private static string Corrected(string text) => text + " korrigiert";

    private static Task<bool> StartPreparation(CorrectionCoordinator coordinator, ComposerSnapshot snapshot)
    {
        var preparation = GetPreparation(coordinator);
        preparation.Observe(snapshot, eligible: true, allowStart: false);
        var changedAt = typeof(CorrectionPreparation).GetField("_changedAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var time = (TimeProvider)typeof(CorrectionPreparation)
            .GetField("_time", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(preparation)!;
        changedAt.SetValue(preparation, time.GetTimestamp() - time.TimestampFrequency * 2);
        preparation.Observe(snapshot, eligible: true);
        return GetOperationTask(preparation);
    }

    private static Task<bool> GetOperationTask(CorrectionPreparation preparation)
    {
        var operation = typeof(CorrectionPreparation)
            .GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(preparation)!;
        return (Task<bool>)operation.GetType()
            .GetProperty("Task", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(operation)!;
    }

    private static CorrectionPreparation GetPreparation(CorrectionCoordinator coordinator) =>
        (CorrectionPreparation)typeof(CorrectionCoordinator)
            .GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator)!;

    private static Task InvokeManualCorrection(CorrectionCoordinator coordinator) =>
        (Task)typeof(CorrectionCoordinator)
            .GetMethod("CorrectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [false])!;

    private static void InvokeCoordinatorMethod(CorrectionCoordinator coordinator, string methodName, params object?[] args)
    {
        var result = typeof(CorrectionCoordinator)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, args);
        if (result is Task task)
        {
            Assert.True(PumpUntil(Dispatcher.CurrentDispatcher, () => task.IsCompleted, DispatcherTimeout));
            task.GetAwaiter().GetResult();
        }
    }

    private static bool PumpUntil(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        if (condition()) return true;

        var frame = new DispatcherFrame();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
            {
                frame.Continue = false;
            }
        }, dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }

    private static void PumpFor(Dispatcher dispatcher, TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Background,
            (_, _) => frame.Continue = false, dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void RunOnSta(Action<Dispatcher> action, TimeSpan? timeout = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            try
            {
                action(dispatcher);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                dispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(timeout ?? TimeSpan.FromSeconds(20)), "Der Coordinator-Vorbereitungstest hat das Zeitlimit überschritten.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class Scenario : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "CodexLanguageFix.Tests", Guid.NewGuid().ToString("N"));
        private readonly TrayController _tray;
        private readonly OverlayWindow _overlay;

        public Scenario(ICorrectionProvider? provider = null, IOpenAiConnection? connection = null)
        {
            Directory.CreateDirectory(_directory);
            var localizer = new AppLocalizer("en");
            var settings = new AppSettings
            {
                Enabled = true,
                PrepareLunaCorrections = true
            };
            var settingsService = new SettingsService(_directory);
            _tray = new TrayController(settings, settingsService, new AutostartService(localizer), localizer);
            var notifyIcon = (System.Windows.Forms.NotifyIcon)typeof(TrayController)
                .GetField("_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(_tray)!;
            notifyIcon.Visible = false;

            _overlay = new OverlayWindow(localizer);
            Composer = new ComposerAccessor();
            Luna = new CachingProvider(CorrectionProviderKind.Luna);
            Coordinator = new CorrectionCoordinator(
                Composer,
                provider ?? Luna,
                connection ?? new OpenAiConnection(),
                _overlay,
                _tray,
                new DiagnosticLogger(_directory),
                Dispatcher.CurrentDispatcher,
                localizer);
        }

        public string DirectoryPath => _directory;
        public string LastStatus => _tray.LastStatus;
        public ComposerAccessor Composer { get; }
        public CachingProvider Luna { get; }
        public CorrectionCoordinator Coordinator { get; }
        public CorrectionPreparation Preparation => GetPreparation(Coordinator);

        public void Dispose()
        {
            Coordinator.Dispose();
            _tray.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class ComposerAccessor : IComposerAccessor
    {
        public ComposerSnapshot? Current { get; set; }
        public Action? OnCapture { get; set; }
        public Action? OnPoll { get; set; }
        public List<Write> Writes { get; } = [];

        public ComposerSnapshot? TryCaptureFocusedComposer() => Current;
        public ComposerSnapshot? TryCaptureFocusedComposerWithoutLayout(bool requireKeyboardFocus = false)
        {
            OnCapture?.Invoke();
            return Current;
        }
        public ComposerSnapshot? TryPollFocusedComposer() { OnPoll?.Invoke(); return Current; }
        public void InvalidateLayout() { }

        public ComposerSnapshot? TryRefresh(ComposerSnapshot snapshot) =>
            snapshot.SameEditor(Current) ? Current : null;

        public bool TryReplace(ComposerSnapshot snapshot, string expectedText, string replacement)
        {
            if (!snapshot.SameEditor(Current) || Current!.Text != expectedText)
                return false;

            Writes.Add(new Write(expectedText, replacement));
            Current = Current with { Text = replacement };
            return true;
        }
    }

    private sealed record Write(string ExpectedText, string Replacement);

    private sealed class CachingProvider(CorrectionProviderKind kind) : ICorrectionProvider
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, CorrectionProviderResult> _cache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _immediate = new(StringComparer.Ordinal);
        private readonly HashSet<string> _fail = new(StringComparer.Ordinal);

        public CorrectionProviderKind Kind => kind;
        public int RequestCount { get; private set; }
        public int InferenceCount { get; private set; }

        public Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                RequestCount++;
                if (_fail.Remove(text))
                    return Task.FromException<CorrectionProviderResult>(new LanguageFixException("Provider failure"));
                if (_cache.TryGetValue(text, out var cached))
                    return Task.FromResult(cached with { CacheHit = true });
                if (_pending.TryGetValue(text, out var inFlight))
                    return inFlight.Completion.Task;
                if (_immediate.Remove(text, out var corrected))
                {
                    InferenceCount++;
                    var result = CreateResult(corrected);
                    _cache[text] = result;
                    return Task.FromResult(result);
                }

                InferenceCount++;
                var pending = new Pending(text, cancellationToken);
                _pending.Add(text, pending);
                return pending.Completion.Task;
            }
        }

        public Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CorrectionProviderHealth(kind, TimeSpan.Zero));

        public Pending GetPending(string text)
        {
            lock (_gate) return _pending[text];
        }

        public void Complete(string text)
        {
            Pending pending;
            CorrectionProviderResult result;
            lock (_gate)
            {
                pending = _pending[text];
                result = CreateResult(Corrected(text));
                _cache[text] = result;
            }
            pending.Completion.SetResult(result);
        }

        public void CompleteImmediately(string text)
        {
            lock (_gate) _immediate[text] = Corrected(text);
        }

        public void FailImmediately(string text)
        {
            lock (_gate) _fail.Add(text);
        }

        private CorrectionProviderResult CreateResult(string correctedText) =>
            new(correctedText, 1, kind, 200, TimeSpan.FromMilliseconds(5));

        public sealed record Pending(string Text, CancellationToken Token)
        {
            public TaskCompletionSource<CorrectionProviderResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class OpenAiConnection : IOpenAiConnection
    {
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WarmUpAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
