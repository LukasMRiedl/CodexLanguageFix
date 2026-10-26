using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexLanguageFix.Core;
using CodexLanguageFix.UI;

namespace CodexLanguageFix.Tests;

[CollectionDefinition("Settings window native input", DisableParallelization = true)]
public sealed class SettingsWindowNativeInputCollection { }

[Collection("Settings window native input")]
public sealed class SettingsWindowInputTests
{
    private const string OptInVariable = "CODEX_LANGUAGE_FIX_SETTINGS_INPUT_TEST";
    private const ushort VkLeftControl = 0xA2;
    private const ushort VkRightMenu = 0xA5;
    private const ushort VkLeftShift = 0xA0;
    private const ushort VkK = 0x4B;
    private const ushort VkJ = 0x4A;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    [Fact]
    public void AltGrK_CanBeCapturedAndSavedByTheSettingsWindow()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1") return;

        RunNativeCaptureCase(
            [
                KeyInput(VkRightMenu),
                KeyInput(VkK),
                KeyInput(VkK, keyUp: true),
                KeyInput(VkRightMenu, keyUp: true),
                KeyInput(VkLeftControl, keyUp: true)
            ],
            [
                KeyInput(VkK, keyUp: true),
                KeyInput(VkRightMenu, keyUp: true),
                KeyInput(VkLeftControl, keyUp: true)
            ],
            [VkLeftControl, VkRightMenu, VkK],
            "Strg+Alt+K",
            new CorrectionShortcut(0x0003, VkK),
            requireGermanKeyboardLayout: true);
    }

    [Fact]
    public void CtrlShiftJ_CanBeCapturedAndSavedByTheSettingsWindow()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1") return;

        RunNativeCaptureCase(
            [
                KeyInput(VkLeftControl),
                KeyInput(VkLeftShift),
                KeyInput(VkJ),
                KeyInput(VkJ, keyUp: true),
                KeyInput(VkLeftShift, keyUp: true),
                KeyInput(VkLeftControl, keyUp: true)
            ],
            [
                KeyInput(VkJ, keyUp: true),
                KeyInput(VkLeftShift, keyUp: true),
                KeyInput(VkLeftControl, keyUp: true)
            ],
            [VkLeftControl, VkLeftShift, VkJ],
            "Strg+Umschalt+J",
            new CorrectionShortcut(0x0006, VkJ),
            requireGermanKeyboardLayout: false);
    }

    private static void RunNativeCaptureCase(
        NativeInput[] chord,
        NativeInput[] releaseKeys,
        int[] keyCodesToCheck,
        string expectedDisplay,
        CorrectionShortcut expectedShortcut,
        bool requireGermanKeyboardLayout)
    {
        RunSta(dispatcher =>
        {
            var originalForeground = GetForegroundWindow();
            CorrectionShortcut? savedShortcut = null;
            var saveCalls = 0;
            var settings = new SettingsWindow(
                new AppLocalizer("de"),
                CorrectionShortcut.Default,
                "Native capture input test",
                shortcut =>
                {
                    savedShortcut = shortcut;
                    saveCalls++;
                    return null;
                })
            {
                Title = "Shortcut recording input test",
                ShowInTaskbar = true
            };
            var nativeInputWasAttempted = false;
            var nativeKeysReleased = false;
            var focusRestored = originalForeground == nint.Zero;
            Exception? interactionFailure = null;
            nint ownWindow = nint.Zero;

            settings.ContentRendered += (_, _) =>
            {
                try
                {
                    ownWindow = new WindowInteropHelper(settings).Handle;
                    Assert.NotEqual(nint.Zero, ownWindow);
                    _ = settings.Activate();
                    Assert.True(
                        WaitForForegroundWindow(dispatcher, ownWindow, TimeSpan.FromSeconds(120)),
                        "Das eigene Einstellungsfenster wurde nicht innerhalb von 120 Sekunden zum Vordergrund.");
                    Assert.Equal(ownWindow, GetForegroundWindow());

                    var recordButton = (Button)settings.FindName("RecordButton")!;
                    var shortcutDisplay = (TextBox)settings.FindName("ShortcutDisplayTextBox")!;
                    var saveButton = (Button)settings.FindName("SaveButton")!;

                    recordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.NotNull(Keyboard.Focus(settings));
                    Assert.Equal(ownWindow, GetForegroundWindow());

                    if (requireGermanKeyboardLayout)
                    {
                        var activeLayout = unchecked((ushort)(GetKeyboardLayout(0).ToInt64() & 0xFFFF));
                        Assert.Equal(0x0007, activeLayout & 0x03FF);
                    }

                    AssertAllKeysUp(keyCodesToCheck);
                    nativeInputWasAttempted = true;
                    var sent = SendInputOnlyToForegroundWindow(ownWindow, chord);
                    Assert.Equal((uint)chord.Length, sent);

                    Pump(dispatcher, TimeSpan.FromMilliseconds(200));
                    Assert.Equal(expectedDisplay, shortcutDisplay.Text);
                    AssertAllKeysUp(keyCodesToCheck);
                    nativeKeysReleased = true;

                    saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception)
                {
                    interactionFailure = exception;
                }
                finally
                {
                    try
                    {
                        if (nativeInputWasAttempted
                            && !AreKeysUp(keyCodesToCheck)
                            && ownWindow != nint.Zero
                            && GetForegroundWindow() == ownWindow)
                        {
                            _ = SendInputOnlyToForegroundWindow(ownWindow, releaseKeys);
                            Pump(dispatcher, TimeSpan.FromMilliseconds(100));
                        }
                    }
                    catch (Exception cleanupException)
                    {
                        interactionFailure ??= cleanupException;
                    }
                    finally
                    {
                        nativeKeysReleased = AreKeysUp(keyCodesToCheck);
                        if (settings.IsVisible)
                        {
                            settings.Close();
                        }
                    }
                }
            };

            try
            {
                var dialogResult = settings.ShowDialog();

                if (interactionFailure is not null)
                {
                    ExceptionDispatchInfo.Capture(interactionFailure).Throw();
                }

                Assert.True(dialogResult, "Der Speichern-Klick hat den Dialog nicht erfolgreich geschlossen.");
                Assert.Equal(1, saveCalls);
                Assert.True(savedShortcut.HasValue);
                Assert.Equal(expectedShortcut, savedShortcut.Value);
            }
            finally
            {
                if (settings.IsVisible)
                {
                    settings.Close();
                }

                if (originalForeground != nint.Zero && IsWindow(originalForeground))
                {
                    _ = SetForegroundWindow(originalForeground);
                    focusRestored = WaitForForegroundWindow(dispatcher, originalForeground, TimeSpan.FromSeconds(2));
                }
            }

            Assert.True(nativeKeysReleased, "Die injizierten Modifikatortasten wurden nicht freigegeben.");
            Assert.True(focusRestored, "Der ursprüngliche Vordergrund wurde nach dem Eingabetest nicht wiederhergestellt.");
        });
    }

    private static uint SendInputOnlyToForegroundWindow(nint ownWindow, NativeInput[] inputs)
    {
        Assert.Equal(ownWindow, GetForegroundWindow());
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
    }

    private static NativeInput KeyInput(ushort virtualKey, bool keyUp = false) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = (keyUp ? KeyEventKeyUp : 0u)
                    | (virtualKey == VkRightMenu ? KeyEventExtendedKey : 0u)
            }
        }
    };

    private static void AssertAllKeysUp(int[] keyCodes) =>
        Assert.True(AreKeysUp(keyCodes), "Vor dem Test dürfen keine der verwendeten Tasten gedrückt sein.");

    private static bool AreKeysUp(int[] keyCodes) => keyCodes.All(keyCode => !IsKeyDown(keyCode));

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static bool WaitForForegroundWindow(Dispatcher dispatcher, nint ownWindow, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (GetForegroundWindow() != ownWindow && elapsed.Elapsed < timeout)
        {
            Pump(dispatcher, TimeSpan.FromMilliseconds(100));
        }

        return GetForegroundWindow() == ownWindow;
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(150)), "Der native Einstellungsfenster-Test hat das Zeitlimit überschritten.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

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
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
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