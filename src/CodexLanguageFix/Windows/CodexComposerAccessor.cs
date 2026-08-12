using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Windows;

public sealed class CodexComposerAccessor : IComposerAccessor
{
    private const int AnimatedStepDelayMilliseconds = 4;
    public string LastWriteStatus { get; private set; } = "not_attempted";
    internal static int NativeInputSize => UnicodeInput.StructSize;
    internal static int AnimatedBatchSize(int textLength) => UnicodeInput.CalculateAnimatedBatchSize(textLength);

    public ComposerSnapshot? TryCaptureFocusedComposer()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                var editable = FindEditableAncestor(focused);
                var focusedHost = editable is null ? null : TryGetComposerHost(editable);
                if (editable is not null && focusedHost is not null && !IsPasswordField(editable))
                {
                    return CreateSnapshot(editable, focusedHost.Value);
                }
            }

            return TryCaptureActiveWindowComposer(focused);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static ComposerSnapshot? TryCaptureActiveWindowComposer(AutomationElement? focused)
    {
        var root = FindDocumentRoot(focused);
        if (root is null)
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == nint.Zero)
            {
                return null;
            }

            root = AutomationElement.FromHandle(foregroundWindow);
        }

        var host = TryGetComposerHost(root);
        if (host is null)
        {
            return null;
        }

        var focusableElements = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true));
        AutomationElement? best = null;
        var bestBottom = double.MinValue;
        foreach (AutomationElement element in focusableElements)
        {
            if (!IsComposerShapeForHost(element, host.Value)
                || IsPasswordField(element)
                || !element.Current.IsEnabled
                || element.Current.IsOffscreen)
            {
                continue;
            }

            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 40 || bounds.Height < 20)
            {
                continue;
            }

            if (bounds.Bottom > bestBottom)
            {
                best = element;
                bestBottom = bounds.Bottom;
            }
        }

        return best is null ? null : CreateSnapshot(best, host.Value);
    }

    private static AutomationElement? FindDocumentRoot(AutomationElement? element)
    {
        var current = element;
        for (var depth = 0; depth < 20 && current is not null; depth++)
        {
            if (current.Current.ControlType == ControlType.Document
                && string.Equals(current.Current.AutomationId, "RootWebArea", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return null;
    }

    internal static bool IsComposerShapeForHost(
        ControlType type,
        string? className,
        ComposerHost host)
    {
        var classes = className ?? string.Empty;
        return host switch
        {
            ComposerHost.Codex => type == ControlType.Edit
                && classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(token => string.Equals(token, "ProseMirror", StringComparison.OrdinalIgnoreCase)),
            ComposerHost.Antigravity => type == ControlType.ComboBox
                && classes.Contains("cursor-text", StringComparison.OrdinalIgnoreCase)
                && classes.Contains("overflow-y-auto", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsComposerShapeForHost(AutomationElement element, ComposerHost host) =>
        IsComposerShapeForHost(
            element.Current.ControlType,
            element.Current.ClassName,
            host);

    public ComposerSnapshot? TryRefresh(ComposerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.NativeElement is not AutomationElement element)
        {
            return null;
        }

        try
        {
            if (!IsSameRuntimeId(element.GetRuntimeId(), snapshot.RuntimeId))
            {
                return null;
            }

            return CreateSnapshot(element, snapshot.Host);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public bool TryReplace(ComposerSnapshot snapshot, string expectedText, string replacement)
    {
        LastWriteStatus = "started";
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(expectedText);
        ArgumentNullException.ThrowIfNull(replacement);
        if (snapshot.NativeElement is not AutomationElement element)
        {
            LastWriteStatus = "invalid_native_element";
            return false;
        }

        try
        {
            var current = TryRefresh(snapshot);
            if (current is null || !string.Equals(current.Text, expectedText, StringComparison.Ordinal))
            {
                LastWriteStatus = current is null ? "refresh_unavailable" : "expected_text_mismatch";
                return false;
            }

            if (EnsureComposerFocus(element) && UnicodeInput.ReplaceFocusedText(replacement))
            {
                var unicodeObserved = WaitForText(element, replacement);
                LastWriteStatus = unicodeObserved ? "ok_unicode_animated" : "unicode_not_observed";
                return unicodeObserved;
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject))
            {
                var valuePattern = (ValuePattern)valuePatternObject;
                if (!valuePattern.Current.IsReadOnly)
                {
                    ReplaceWithValueAnimation(valuePattern, replacement);
                    var observed = WaitForText(element, replacement);
                    LastWriteStatus = observed ? "ok_value_animated_fallback" : "value_not_observed";
                    return observed;
                }
            }

            LastWriteStatus = "animated_write_unavailable";
            return false;
        }
        catch (ElementNotAvailableException)
        {
            LastWriteStatus = "element_unavailable_exception";
            return false;
        }
        catch (InvalidOperationException)
        {
            LastWriteStatus = "invalid_operation_exception";
            return false;
        }
        catch (Win32Exception)
        {
            LastWriteStatus = "win32_exception";
            return false;
        }
        catch (ArgumentException)
        {
            LastWriteStatus = "argument_exception";
            return false;
        }
        catch (COMException)
        {
            LastWriteStatus = "com_exception";
            return false;
        }
    }

    private static AutomationElement? FindEditableAncestor(AutomationElement start)
    {
        var current = start;
        for (var depth = 0; depth < 16 && current is not null; depth++)
        {
            if (IsEditable(current))
            {
                return current;
            }

            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return null;
    }

    private static bool IsEditable(AutomationElement element)
    {
        var type = element.Current.ControlType;
        var className = element.Current.ClassName;
        var automationId = element.Current.AutomationId;
        if (!IsSupportedComposerShape(type, className, automationId))
        {
            return false;
        }

        if (!element.Current.IsEnabled || !element.Current.IsKeyboardFocusable)
        {
            return false;
        }

        return element.TryGetCurrentPattern(ValuePattern.Pattern, out _)
            || element.TryGetCurrentPattern(TextPattern.Pattern, out _);
    }

    internal static bool IsSupportedComposerShape(ControlType type, string? className, string? automationId)
    {
        var isProseMirror = type == ControlType.Group
            && (className ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, "ProseMirror", StringComparison.OrdinalIgnoreCase));
        var isRootWebArea = type == ControlType.Document
            && string.Equals(automationId, "RootWebArea", StringComparison.OrdinalIgnoreCase);
        var isAntigravityComposer = type == ControlType.ComboBox
            && (className ?? string.Empty).Contains("cursor-text", StringComparison.OrdinalIgnoreCase)
            && (className ?? string.Empty).Contains("overflow-y-auto", StringComparison.OrdinalIgnoreCase);
        return !isRootWebArea
            && (type == ControlType.Edit || type == ControlType.Document || isProseMirror || isAntigravityComposer);
    }

    private static bool IsPasswordField(AutomationElement element)
    {
        try
        {
            return element.Current.IsPassword;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static ComposerHost? TryGetComposerHost(AutomationElement element)
    {
        try
        {
            using var process = Process.GetProcessById(element.Current.ProcessId);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                && (path.Contains(@"\WindowsApps\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
                    || path.Contains(@"\OpenAI\Codex\", StringComparison.OrdinalIgnoreCase)))
            {
                return ComposerHost.Codex;
            }

            if (string.Equals(process.ProcessName, "Antigravity", StringComparison.OrdinalIgnoreCase)
                && path.Contains(@"\Programs\antigravity\", StringComparison.OrdinalIgnoreCase))
            {
                return ComposerHost.Antigravity;
            }

            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static ComposerSnapshot? CreateSnapshot(AutomationElement element, ComposerHost host)
    {
        var text = ReadText(element);
        if (text is null)
        {
            return null;
        }

        var editorBounds = element.Current.BoundingRectangle;
        if (editorBounds.IsEmpty || editorBounds.Width < 40 || editorBounds.Height < 20 || double.IsNaN(editorBounds.X) || double.IsInfinity(editorBounds.X))
        {
            return null;
        }

        var surfaceBounds = FindComposerSurfaceBounds(element, editorBounds);
        var toolbar = FindToolbarContext(element, surfaceBounds, host);
        return new ComposerSnapshot(
            element,
            element.GetRuntimeId(),
            text,
            surfaceBounds,
            element.Current.ProcessId,
            toolbar.RightControlBounds,
            host);
    }

    private static Rect FindComposerSurfaceBounds(AutomationElement composer, Rect editorBounds)
    {
        var current = composer;
        for (var depth = 0; depth < 6; depth++)
        {
            var parent = TreeWalker.ControlViewWalker.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent;
            if (current.Current.ControlType == ControlType.Document)
            {
                break;
            }

            var candidate = current.Current.BoundingRectangle;
            if (IsComposerSurfaceCandidate(editorBounds, candidate))
            {
                return candidate;
            }
        }

        return editorBounds;
    }

    internal static bool IsComposerSurfaceCandidate(Rect editorBounds, Rect candidate) =>
        !editorBounds.IsEmpty
        && !candidate.IsEmpty
        && candidate.Width >= editorBounds.Width + 80
        && candidate.Height >= editorBounds.Height
        && candidate.Height <= Math.Max(240, editorBounds.Height + 160)
        && candidate.Left <= editorBounds.Left
        && candidate.Top <= editorBounds.Top
        && candidate.Right >= editorBounds.Right
        && candidate.Bottom >= editorBounds.Bottom;

    private static ToolbarContext FindToolbarContext(
        AutomationElement composer,
        Rect composerBounds,
        ComposerHost host)
    {
        try
        {
            var searchRoot = composer;
            for (var depth = 0; depth < 12; depth++)
            {
                var parent = TreeWalker.ControlViewWalker.GetParent(searchRoot);
                if (parent is null)
                {
                    break;
                }

                searchRoot = parent;
                if (searchRoot.Current.ControlType == ControlType.Document
                    && string.Equals(searchRoot.Current.AutomationId, "RootWebArea", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            var buttons = searchRoot.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var minimumLeft = composerBounds.Left + composerBounds.Width * (
                host == ComposerHost.Codex ? 0.35 : 0.55);
            var maximumTopDistance = Math.Max(48, composerBounds.Height);
            Rect? best = null;
            foreach (AutomationElement button in buttons)
            {
                var bounds = button.Current.BoundingRectangle;
                if (bounds.IsEmpty
                    || bounds.Width < 24
                    || bounds.Left < minimumLeft
                    || bounds.Left >= composerBounds.Right
                    || bounds.Bottom < composerBounds.Top - 12
                    || bounds.Top > composerBounds.Bottom + maximumTopDistance)
                {
                    continue;
                }

                var isAnchor = host == ComposerHost.Antigravity
                    || IsCodexModelAnchor(bounds);
                if (isAnchor && IsBetterToolbarAnchor(bounds, best, host))
                {
                    best = bounds;
                }
            }

            return new ToolbarContext(best);
        }
        catch (ElementNotAvailableException)
        {
            return default;
        }
        catch (InvalidOperationException)
        {
            return default;
        }
        catch (ArgumentException)
        {
            return default;
        }
        catch (COMException)
        {
            return default;
        }
    }

    internal static bool IsCodexModelAnchor(Rect bounds) =>
        !bounds.IsEmpty
        && bounds.Height >= 24
        && bounds.Width > bounds.Height * 1.5;

    internal static bool IsBetterToolbarAnchor(
        Rect candidate,
        Rect? current,
        ComposerHost host) =>
        current is null
        || (host == ComposerHost.Codex
            ? candidate.Left > current.Value.Left
            : candidate.Left < current.Value.Left);

    private readonly record struct ToolbarContext(Rect? RightControlBounds);

    private static string? ReadText(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject))
        {
            return ((ValuePattern)valuePatternObject).Current.Value;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject))
        {
            return ((TextPattern)textPatternObject).DocumentRange.GetText(-1);
        }

        return null;
    }

    private static bool IsFocusedElementOrAncestor(AutomationElement expected)
    {
        var current = AutomationElement.FocusedElement;
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            if (IsSameRuntimeId(current.GetRuntimeId(), expected.GetRuntimeId()))
            {
                return true;
            }

            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return false;
    }

    private static bool EnsureComposerFocus(AutomationElement element)
    {
        if (IsFocusedElementOrAncestor(element))
        {
            return true;
        }

        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        if (foregroundProcessId != element.Current.ProcessId
            && foregroundProcessId != Environment.ProcessId)
        {
            return false;
        }

        element.SetFocus();
        return IsFocusedElementOrAncestor(element);
    }

    private static bool WaitForText(AutomationElement element, string expected)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (string.Equals(ReadText(element), expected, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    private static void ReplaceWithValueAnimation(ValuePattern valuePattern, string text)
    {
        if (text.Length == 0)
        {
            valuePattern.SetValue(string.Empty);
            return;
        }

        var batchSize = AnimatedBatchSize(text.Length);
        for (var length = batchSize; length < text.Length; length += batchSize)
        {
            valuePattern.SetValue(text[..length]);
            Thread.Sleep(AnimatedStepDelayMilliseconds);
        }

        valuePattern.SetValue(text);
    }

    private static bool IsSameRuntimeId(IReadOnlyList<int> left, IReadOnlyList<int> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    private static class UnicodeInput
    {
        private const ushort VirtualKeyControl = 0x11;
        private const ushort VirtualKeyA = 0x41;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventUnicode = 0x0004;
        private const int InputKeyboard = 1;
        private const int TargetAnimationSteps = 12;
        private const int MaximumBatchSize = 512;
        public static int StructSize => Marshal.SizeOf<INPUT>();

        public static bool ReplaceFocusedText(string text)
        {
            var selectAll = new[]
            {
                Key(VirtualKeyControl, 0),
                Key(VirtualKeyA, 0),
                Key(VirtualKeyA, KeyEventKeyUp),
                Key(VirtualKeyControl, KeyEventKeyUp)
            };
            if (SendInput((uint)selectAll.Length, selectAll, Marshal.SizeOf<INPUT>()) != selectAll.Length)
            {
                return false;
            }

            var batchSize = CalculateAnimatedBatchSize(text.Length);
            var chunks = text.Chunk(batchSize).ToArray();
            for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var inputs = new INPUT[chunk.Length * 2];
                for (var index = 0; index < chunk.Length; index++)
                {
                    inputs[index * 2] = UnicodeKey(chunk[index], 0);
                    inputs[index * 2 + 1] = UnicodeKey(chunk[index], KeyEventKeyUp);
                }

                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
                {
                    return false;
                }

                if (chunkIndex + 1 < chunks.Length)
                {
                    Thread.Sleep(AnimatedStepDelayMilliseconds);
                }
            }

            return true;
        }

        internal static int CalculateAnimatedBatchSize(int textLength)
        {
            if (textLength <= 0)
            {
                return 1;
            }

            return Math.Clamp(
                (int)Math.Ceiling(textLength / (double)TargetAnimationSteps),
                1,
                MaximumBatchSize);
        }

        private static INPUT Key(ushort virtualKey, uint flags) => new()
        {
            Type = InputKeyboard,
            Union = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT { VirtualKey = virtualKey, Flags = flags }
            }
        };

        private static INPUT UnicodeKey(char character, uint flags) => new()
        {
            Type = InputKeyboard,
            Union = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT { ScanCode = character, Flags = flags | KeyEventUnicode }
            }
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int Type;
            public INPUTUNION Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;

            // Der größte Union-Zweig bestimmt die native INPUT-Größe (40 Byte auf x64).
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }
    }
}
