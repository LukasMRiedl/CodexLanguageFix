using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

[CollectionDefinition("Correction hotkey", DisableParallelization = true)]
public sealed class CorrectionHotkeyCollection { }

[Collection("Correction hotkey")]
public sealed class CorrectionHotkeyTests
{
    private const int WmHotkey = 0x0312;
    private const uint ModControlAlt = 0x0001 | 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint OemComma = 0xBC;
    private const int HotkeyId = 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly nint HwndMessage = new(-3);

    [Fact]
    public void Registration_RejectsConflictAndReleasesTheNativeHotkey()
    {
        RunSta(dispatcher =>
        {
            using var blocker = CreateMessageWindow("CorrectionHotkey.Blocker");
            Assert.True(RegisterHotKey(blocker.Handle, 71, ModControlAlt | ModNoRepeat, OemComma),
                $"Der Test-Hotkey ist bereits belegt (Win32-Fehler {Marshal.GetLastWin32Error()}).");

            try
            {
                using var hotkey = new CorrectionHotkey();
                Assert.False(hotkey.SetEnabled(true));
                Assert.Equal(ErrorHotkeyAlreadyRegistered, Marshal.GetLastWin32Error());
                Assert.True(hotkey.SetEnabled(false));

                Assert.True(UnregisterHotKey(blocker.Handle, 71),
                    $"Der Test-Hotkey konnte nicht freigegeben werden (Win32-Fehler {Marshal.GetLastWin32Error()}).");

                Assert.True(hotkey.SetEnabled(true));
                Assert.True(hotkey.SetEnabled(true));
                Assert.True(hotkey.SetEnabled(false));
                Assert.True(hotkey.SetEnabled(false));

                using var probe = CreateMessageWindow("CorrectionHotkey.ReleaseProbe");
                Assert.True(RegisterHotKey(probe.Handle, 72, ModControlAlt | ModNoRepeat, OemComma),
                    $"SetEnabled(false) hat den Hotkey nicht freigegeben (Win32-Fehler {Marshal.GetLastWin32Error()}).");
                Assert.True(UnregisterHotKey(probe.Handle, 72));

                hotkey.Dispose();
                Assert.False(hotkey.SetEnabled(true));
            }
            finally
            {
                _ = UnregisterHotKey(blocker.Handle, 71);
            }
        });
    }

    [Fact]
    public void WmHotkey_OnlyMatchingRegisteredMessageRaisesCorrection()
    {
        RunSta(dispatcher =>
        {
            using var hotkey = new CorrectionHotkey();
            Assert.True(hotkey.SetEnabled(true),
                $"Der Test-Hotkey konnte nicht registriert werden (Win32-Fehler {Marshal.GetLastWin32Error()}).");
            var requests = 0;
            hotkey.CorrectRequested += (_, _) => requests++;
            var hwnd = GetSourceHandle(hotkey);
            var matchingLParam = MakeHotkeyLParam(ModControlAlt, OemComma);

            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId, matchingLParam));
            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId + 1, matchingLParam));
            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId, MakeHotkeyLParam(0x0001, OemComma)));
            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId, MakeHotkeyLParam(ModControlAlt, 0x51)));
            Assert.True(PostMessage(hwnd, WmHotkey + 1, HotkeyId, matchingLParam));

            Pump(dispatcher, TimeSpan.FromMilliseconds(150));

            Assert.Equal(1, requests);
        });
    }

    [Fact]
    public void DisablingHotkey_IgnoresAlreadyQueuedAndLaterMessages()
    {
        RunSta(dispatcher =>
        {
            using var hotkey = new CorrectionHotkey();
            Assert.True(hotkey.SetEnabled(true),
                $"Der Test-Hotkey konnte nicht registriert werden (Win32-Fehler {Marshal.GetLastWin32Error()}).");
            var requests = 0;
            hotkey.CorrectRequested += (_, _) => requests++;
            var hwnd = GetSourceHandle(hotkey);
            var matchingLParam = MakeHotkeyLParam(ModControlAlt, OemComma);

            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId, matchingLParam));
            Assert.True(hotkey.SetEnabled(false));
            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId, matchingLParam));
            Pump(dispatcher, TimeSpan.FromMilliseconds(150));

            Assert.Equal(0, requests);

            Assert.True(hotkey.SetEnabled(true));
            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId, matchingLParam));
            Pump(dispatcher, TimeSpan.FromMilliseconds(100));
            Assert.Equal(1, requests);
        });
    }

    [Fact]
    public void Dispose_UnregistersAndDropsQueuedMessage()
    {
        RunSta(dispatcher =>
        {
            var hotkey = new CorrectionHotkey();
            Assert.True(hotkey.SetEnabled(true),
                $"Der Test-Hotkey konnte nicht registriert werden (Win32-Fehler {Marshal.GetLastWin32Error()}).");
            var requests = 0;
            hotkey.CorrectRequested += (_, _) => requests++;
            var hwnd = GetSourceHandle(hotkey);
            Assert.True(PostMessage(hwnd, WmHotkey, HotkeyId, MakeHotkeyLParam(ModControlAlt, OemComma)));

            hotkey.Dispose();
            Assert.False(hotkey.SetEnabled(true));

            using var probe = CreateMessageWindow("CorrectionHotkey.DisposeProbe");
            Assert.True(RegisterHotKey(probe.Handle, 73, ModControlAlt | ModNoRepeat, OemComma),
                $"Dispose() hat den globalen Hotkey nicht freigegeben (Win32-Fehler {Marshal.GetLastWin32Error()}).");
            Assert.True(UnregisterHotKey(probe.Handle, 73));

            Pump(dispatcher, TimeSpan.FromMilliseconds(150));
            Assert.Equal(0, requests);
            hotkey.Dispose();
        });
    }

    [Fact]
    public void AltGrHotkeyInput_IsSingleShotReleasesModifiersAndRestoresFocus()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_HOTKEY_INPUT_TEST") != "1") return;

        RunSta(dispatcher =>
        {
            var originalForeground = GetForegroundWindow();
            var window = new Window
            {
                Title = "Correction hotkey input test",
                Width = 360,
                Height = 100,
                WindowStyle = System.Windows.WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new TextBox { Text = "Eigener Hotkey-Testeditor", Margin = new Thickness(12) }
            };
            var editor = (TextBox)window.Content;
            var hotkey = new CorrectionHotkey();
            var requests = 0;
            var injectedKeysMayBeDown = false;
            var modifiersReleased = false;
            var focusRestored = originalForeground == nint.Zero;

            try
            {
                Assert.True(hotkey.SetEnabled(true),
                    $"Der Test-Hotkey konnte nicht registriert werden (Win32-Fehler {Marshal.GetLastWin32Error()}).");
                hotkey.CorrectRequested += (_, _) => requests++;

                window.Show();
                Assert.True(window.Activate(), "Das eigene Testeditor-Fenster konnte nicht aktiviert werden.");
                Assert.True(editor.Focus(), "Das eigene Testeditor-Feld konnte nicht fokussiert werden.");
                var ownWindow = new WindowInteropHelper(window).Handle;
                Assert.Equal(ownWindow, GetForegroundWindow());

                var activeLayout = unchecked((ushort)(GetKeyboardLayout(0).ToInt64() & 0xFFFF));
                Assert.Equal(0x0007, activeLayout & 0x03FF);
                AssertAllInputKeysUp();

                var repeatedPress = new[]
                {
                    KeyInput(0xA5), // VK_RMENU (AltGr)
                    KeyInput((ushort)OemComma),
                    KeyInput((ushort)OemComma),
                    KeyInput((ushort)OemComma),
                    KeyInput((ushort)OemComma)
                };
                injectedKeysMayBeDown = true;
                Assert.Equal((uint)repeatedPress.Length, SendInput((uint)repeatedPress.Length,
                    repeatedPress, Marshal.SizeOf<NativeInput>()));

                Pump(dispatcher, TimeSpan.FromMilliseconds(500));
                Assert.Equal(1, requests);
            }
            finally
            {
                if (injectedKeysMayBeDown)
                {
                    ReleaseInputKeys();
                    Pump(dispatcher, TimeSpan.FromMilliseconds(100));
                    modifiersReleased = AreInputKeysUp();
                }

                hotkey.Dispose();
                if (window.IsVisible) window.Close();

                if (originalForeground != nint.Zero && IsWindow(originalForeground))
                {
                    _ = SetForegroundWindow(originalForeground);
                    focusRestored = GetForegroundWindow() == originalForeground;
                }
            }

            Assert.True(modifiersReleased, "Die injizierten Ctrl-, AltGr- und Kommatasten wurden nicht freigegeben.");
            Assert.True(focusRestored, "Der ursprüngliche Vordergrund wurde nach dem Eingabetest nicht wiederhergestellt.");
        });
    }

    private static HwndSource CreateMessageWindow(string name) => new(new HwndSourceParameters(name)
    {
        ParentWindow = HwndMessage,
        WindowStyle = 0
    });

    private static nint GetSourceHandle(CorrectionHotkey hotkey) =>
        ((HwndSource)typeof(CorrectionHotkey)
            .GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(hotkey)!).Handle;

    private static nint MakeHotkeyLParam(uint modifiers, uint virtualKey) =>
        (nint)((virtualKey << 16) | modifiers);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Der STA-Hotkey-Test hat das Zeitlimit überschritten.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static NativeInput KeyInput(ushort virtualKey, bool keyUp = false) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = (keyUp ? 0x0002u : 0u) | (virtualKey == 0xA5 ? 0x0001u : 0u)
            }
        }
    };

    private static void ReleaseInputKeys()
    {
        NativeInput[] release = [KeyInput((ushort)OemComma, true), KeyInput(0xA5, true), KeyInput(0xA2, true)];
        _ = SendInput((uint)release.Length, release, Marshal.SizeOf<NativeInput>());
    }

    private static void AssertAllInputKeysUp() =>
        Assert.True(AreInputKeysUp(), "Vor dem Test werden keine bereits gedrückten Ctrl-, AltGr- oder Kommatasten übernommen.");

    private static bool AreInputKeysUp() =>
        !IsKeyDown(0xA2) && !IsKeyDown(0xA5) && !IsKeyDown((ushort)OemComma);

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);
}
