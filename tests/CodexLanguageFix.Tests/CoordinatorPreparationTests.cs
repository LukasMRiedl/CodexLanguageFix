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

public sealed class CoordinatorPreparationTests
{
    private static readonly TimeSpan DispatcherTimeout = TimeSpan.FromSeconds(4);

    [Fact]
    public void ManualRequestAdoptsRunningPreparationAndWritesOnlyAfterTheClick()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario(CorrectionProviderKind.Luna);
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
            using var scenario = new Scenario(CorrectionProviderKind.Luna);
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
            using var scenario = new Scenario(CorrectionProviderKind.Luna);
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
            using var scenario = new Scenario(CorrectionProviderKind.Luna);
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
    public void LanguageToolSelectionDoesNotStartBackgroundProviderRequests()
    {
        RunOnSta(dispatcher =>
        {
            using var scenario = new Scenario(CorrectionProviderKind.LanguageTool);
            var snapshot = Snapshot("keine automatische languagetool Anfrage");
            snapshot = snapshot with { Bounds = Rect.Empty };
            scenario.Composer.Current = snapshot;

            InvokeCoordinatorMethod(scenario.Coordinator, "RefreshOverlay");
            PumpFor(dispatcher, CorrectionPreparation.QuietPeriod + TimeSpan.FromMilliseconds(100));

            Assert.False(scenario.Preparation.IsReady(snapshot));
            Assert.Equal(0, scenario.LanguageTool.RequestCount);
            Assert.Equal(0, scenario.Luna.RequestCount);
            Assert.Empty(scenario.Composer.Writes);
        });
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

    private static void InvokeCoordinatorMethod(CorrectionCoordinator coordinator, string methodName) =>
        typeof(CorrectionCoordinator)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, null);

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

    private static void RunOnSta(Action<Dispatcher> action)
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Der Coordinator-Vorbereitungstest hat das Zeitlimit überschritten.");
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

        public Scenario(CorrectionProviderKind selectedProvider)
        {
            Directory.CreateDirectory(_directory);
            var localizer = new AppLocalizer("en");
            var settings = new AppSettings
            {
                Enabled = true,
                PrepareLunaCorrections = true,
                CorrectionProvider = selectedProvider
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
            LanguageTool = new CachingProvider(CorrectionProviderKind.LanguageTool);
            Coordinator = new CorrectionCoordinator(
                Composer,
                [LanguageTool, Luna],
                new OpenAiConnection(),
                _overlay,
                _tray,
                new DiagnosticLogger(_directory),
                Dispatcher.CurrentDispatcher,
                localizer);
        }

        public ComposerAccessor Composer { get; }
        public CachingProvider Luna { get; }
        public CachingProvider LanguageTool { get; }
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
        public List<Write> Writes { get; } = [];

        public ComposerSnapshot? TryCaptureFocusedComposer() => Current;
        public ComposerSnapshot? TryCaptureFocusedComposerWithoutLayout(bool requireKeyboardFocus = false) => Current;
        public ComposerSnapshot? TryPollFocusedComposer() => Current;
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

        public CorrectionProviderKind Kind => kind;
        public int RequestCount { get; private set; }
        public int InferenceCount { get; private set; }

        public Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                RequestCount++;
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
