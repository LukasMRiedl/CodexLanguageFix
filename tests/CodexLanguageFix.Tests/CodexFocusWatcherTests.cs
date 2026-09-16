using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class CodexFocusWatcherTests
{
    [Fact]
    public void IdleWatcher_DoesNotRunNotificationTimer()
    {
        RunSta(dispatcher =>
        {
            using var watcher = CreateIsolatedWatcher(dispatcher);
            var notifications = 0;
            watcher.Changed += (_, _) => notifications++;

            Assert.False(GetTimer(watcher).IsEnabled);
            Pump(dispatcher, TimeSpan.FromMilliseconds(150));

            Assert.False(GetTimer(watcher).IsEnabled);
            Assert.Equal(0, notifications);
        });
    }

    [Fact]
    public void EventBurst_RaisesOneNotificationAndReturnsToIdle()
    {
        RunSta(dispatcher =>
        {
            using var watcher = CreateIsolatedWatcher(dispatcher);
            var notifications = 0;
            watcher.Changed += (_, _) => notifications++;

            RaiseBurst(watcher);
            Assert.Equal(0, notifications);
            Pump(dispatcher, TimeSpan.FromMilliseconds(250));

            Assert.Equal(1, notifications);
            Assert.False(GetTimer(watcher).IsEnabled);
        });
    }

    [Fact]
    public void SeparatedBursts_EachRaiseOneNotification()
    {
        RunSta(dispatcher =>
        {
            using var watcher = CreateIsolatedWatcher(dispatcher);
            var notifications = 0;
            watcher.Changed += (_, _) => notifications++;

            RaiseBurst(watcher);
            Pump(dispatcher, TimeSpan.FromMilliseconds(250));
            Assert.Equal(1, notifications);

            RaiseBurst(watcher);
            Pump(dispatcher, TimeSpan.FromMilliseconds(250));
            Assert.Equal(2, notifications);
            Assert.False(GetTimer(watcher).IsEnabled);
        });
    }

    [Fact]
    public void DisposeBeforeQueuedStart_CancelsNotificationAndIgnoresLaterEvents()
    {
        RunSta(dispatcher =>
        {
            using var watcher = CreateIsolatedWatcher(dispatcher);
            var notifications = 0;
            watcher.Changed += (_, _) => notifications++;

            RaiseBurst(watcher);
            watcher.Dispose();
            RaiseBurst(watcher);
            Pump(dispatcher, TimeSpan.FromMilliseconds(150));

            Assert.Equal(0, notifications);
            Assert.False(GetTimer(watcher).IsEnabled);
        });
    }

    [Fact]
    public void DisposeAfterTimerStarts_CancelsPendingTick()
    {
        RunSta(dispatcher =>
        {
            using var watcher = CreateIsolatedWatcher(dispatcher);
            var notifications = 0;
            var timerWasStarted = false;
            watcher.Changed += (_, _) => notifications++;

            RaiseBurst(watcher);
            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                timerWasStarted = GetTimer(watcher).IsEnabled;
                watcher.Dispose();
            });
            Pump(dispatcher, TimeSpan.FromMilliseconds(150));

            Assert.True(timerWasStarted);
            Assert.Equal(0, notifications);
            Assert.False(GetTimer(watcher).IsEnabled);
        });
    }

    private static CodexFocusWatcher CreateIsolatedWatcher(Dispatcher dispatcher)
    {
        var watcher = new CodexFocusWatcher(dispatcher);
        // Real desktop events would make the injected event counts nondeterministic.
        var hooks = (List<nint>)typeof(CodexFocusWatcher)
            .GetField("_hooks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(watcher)!;
        var unhook = typeof(CodexFocusWatcher)
            .GetMethod("UnhookWinEvent", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var hook in hooks)
            unhook.Invoke(null, [hook]);
        hooks.Clear();
        return watcher;
    }

    private static DispatcherTimer GetTimer(CodexFocusWatcher watcher) =>
        (DispatcherTimer)typeof(CodexFocusWatcher)
            .GetField("_notificationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(watcher)!;

    private static void RaiseBurst(CodexFocusWatcher watcher)
    {
        var callback = typeof(CodexFocusWatcher)
            .GetMethod("OnWinEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var index = 0; index < 100; index++)
            callback.Invoke(watcher, [nint.Zero, 0x0003u, nint.Zero, 0, 0, 0u, 0u]);
    }

    private static void Pump(Dispatcher dispatcher, TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = duration };
        EventHandler stop = (_, _) => frame.Continue = false;
        timeout.Tick += stop;
        timeout.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timeout.Stop();
            timeout.Tick -= stop;
        }
    }

    private static void RunSta(Action<Dispatcher> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                test(dispatcher);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Der Dispatcher-Test hat das Zeitlimit überschritten.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
