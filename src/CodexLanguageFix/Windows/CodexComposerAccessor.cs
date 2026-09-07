using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Windows;

public sealed class CodexComposerAccessor : IComposerAccessor
{
    public string LastWriteStatus { get; private set; } = "not_attempted";
    public ComposerWriteResult? LastWriteResult { get; private set; }
    internal static int NativeInputSize => UnicodeInput.StructSize;

    public ComposerSnapshot? TryCaptureFocusedComposer()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                var editable = FindEditableAncestor(focused);
                var focusedHost = editable is null ? null : TryGetComposerHost(editable);
                if (editable is not null && focusedHost is not null && !IsPasswordField(editable)
                    && IsComposerShapeForHost(editable, focusedHost.Value))
                {
                    return CreateSnapshot(editable, focusedHost.Value);
                }
                // A focused search, rename field or terminal must not redirect to a different composer.
                if (editable is not null)
                    return null;
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
        AutomationElement? candidate = null;
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

            if (candidate is not null)
            {
                return null;
            }

            candidate = element;
        }

        return candidate is null ? null : CreateSnapshot(candidate, host.Value);
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
            ComposerHost.Codex => (type == ControlType.Edit || type == ControlType.Group || type == ControlType.Document)
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
            if (!IsSameRuntimeId(element.GetRuntimeId(), snapshot.RuntimeId)
                || TryGetComposerHost(element) != snapshot.Host)
            {
                return null;
            }

            var refreshed = CreateSnapshot(element, snapshot.Host);
            return snapshot.SameEditor(refreshed) ? refreshed : null;
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
        LastWriteResult = new ComposerWriteResult(ComposerWriteState.Unchanged, expectedText, expectedText, 0, "started");
        if (snapshot.NativeElement is not AutomationElement element)
        {
            return Failed("invalid_native_element");
        }

        try
        {
            var current = TryRefresh(snapshot);
            if (current is null || !string.Equals(current.Text, expectedText, StringComparison.Ordinal))
            {
                return Failed(current is null ? "refresh_unavailable" : "expected_text_mismatch");
            }

            var plan = ComposerEditPlan.Create(expectedText, replacement);
            if (plan is null)
            {
                return Failed("unsafe_edit_plan");
            }

            if (!EnsureComposerFocus(element, snapshot.HostWindow))
            {
                return Failed("focus_changed");
            }

            var target = new NativeEditTarget(snapshot, element);
            if (snapshot.ReadMethod == ComposerReadMethod.ValuePattern)
            {
                plan = new ComposerEditPlan(expectedText, replacement,
                    expectedText == replacement ? [] : [new ComposerTextEdit(0, expectedText, replacement)]);
            }

            LastWriteResult = ComposerWriteTransaction.Apply(plan, target);
            LastWriteStatus = LastWriteResult.Status;
            return LastWriteResult.State == ComposerWriteState.Applied;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException
            or Win32Exception or ArgumentException or COMException)
        {
            return Failed("element_unavailable_or_invalid");
        }

        bool Failed(string status)
        {
            LastWriteStatus = status;
            LastWriteResult = new ComposerWriteResult(ComposerWriteState.Unchanged, expectedText, null, 0, status);
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
        var isProseMirror = (type == ControlType.Group || type == ControlType.Edit || type == ControlType.Document)
            && (className ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, "ProseMirror", StringComparison.OrdinalIgnoreCase));
        var isRootWebArea = type == ControlType.Document
            && string.Equals(automationId, "RootWebArea", StringComparison.OrdinalIgnoreCase);
        var isAntigravityComposer = type == ControlType.ComboBox
            && (className ?? string.Empty).Contains("cursor-text", StringComparison.OrdinalIgnoreCase)
            && (className ?? string.Empty).Contains("overflow-y-auto", StringComparison.OrdinalIgnoreCase);
        return !isRootWebArea
            && (isProseMirror || isAntigravityComposer);
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
        var text = ReadText(element, out var readMethod);
        if (text is null)
        {
            return null;
        }

        var editorBounds = element.Current.BoundingRectangle;
        if (element.Current.IsOffscreen || !element.Current.IsEnabled
            || editorBounds.IsEmpty || editorBounds.Width < 40 || editorBounds.Height < 20
            || double.IsNaN(editorBounds.X) || double.IsInfinity(editorBounds.X))
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
            host,
            FindHostWindow(element),
            editorBounds,
            toolbar.OccupiedBounds,
            readMethod);
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
            if (IsComposerSurfaceCandidate(editorBounds, candidate) && HasAssociatedControls(current, candidate, editorBounds))
            {
                return candidate;
            }
        }

        return editorBounds;
    }

    private static bool HasAssociatedControls(AutomationElement surface, Rect bounds, Rect editor)
    {
        var buttons = surface.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            var controlBounds = button.Current.BoundingRectangle;
            if (!button.Current.IsOffscreen && !controlBounds.IsEmpty
                && bounds.Contains(controlBounds) && !editor.IntersectsWith(controlBounds))
                return true;
        }
        return false;
    }

    internal static bool IsComposerSurfaceCandidate(Rect editorBounds, Rect candidate) =>
        !editorBounds.IsEmpty
        && !candidate.IsEmpty
        && candidate.Width >= editorBounds.Width
        && (candidate.Width >= editorBounds.Width + 24 || candidate.Height >= editorBounds.Height + 24)
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
            for (var depth = 0; depth < 6 && searchRoot.Current.BoundingRectangle != composerBounds; depth++)
            {
                var parent = TreeWalker.ControlViewWalker.GetParent(searchRoot);
                if (parent is null)
                {
                    break;
                }

                if (parent.Current.ControlType == ControlType.Document)
                {
                    break;
                }

                searchRoot = parent;
            }

            if (searchRoot.Current.BoundingRectangle != composerBounds)
            {
                return new ToolbarContext(null, [composer.Current.BoundingRectangle]);
            }

            var controls = searchRoot.FindAll(
                TreeScope.Descendants,
                new OrCondition(
                    new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)));
            var minimumLeft = composerBounds.Left + composerBounds.Width * (
                host == ComposerHost.Codex ? 0.35 : 0.55);
            var maximumTopDistance = Math.Max(48, composerBounds.Height);
            Rect? best = null;
            var occupied = new List<Rect> { composer.Current.BoundingRectangle };
            foreach (AutomationElement button in controls)
            {
                var bounds = button.Current.BoundingRectangle;
                if (!bounds.IsEmpty && !button.Current.IsOffscreen && bounds.IntersectsWith(composerBounds))
                {
                    occupied.Add(bounds);
                }

                if (bounds.IsEmpty
                    || button.Current.ControlType != ControlType.Button
                    || button.Current.IsOffscreen
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

            return new ToolbarContext(best, occupied);
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

    private readonly record struct ToolbarContext(Rect? RightControlBounds, IReadOnlyList<Rect>? OccupiedBounds);

    private static string? ReadText(AutomationElement element) => ReadText(element, out _);

    private static string? ReadText(AutomationElement element, out ComposerReadMethod readMethod)
    {
        var isStructured = IsComposerShapeForHost(element.Current.ControlType, element.Current.ClassName, ComposerHost.Codex)
            || IsComposerShapeForHost(element.Current.ControlType, element.Current.ClassName, ComposerHost.Antigravity);
        if (!isStructured && element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject))
        {
            readMethod = ComposerReadMethod.ValuePattern;
            return ((ValuePattern)valuePatternObject).Current.Value;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject))
        {
            readMethod = ComposerReadMethod.TextPattern;
            return ((TextPattern)textPatternObject).DocumentRange.GetText(-1);
        }

        readMethod = ComposerReadMethod.Unknown;
        return null;
    }

    private static nint FindHostWindow(AutomationElement element)
    {
        var processId = element.Current.ProcessId;
        nint handle = 0;
        var current = element;
        for (var depth = 0; depth < 30 && current is not null && current.Current.ProcessId == processId; depth++)
        {
            if (current.Current.NativeWindowHandle != 0)
            {
                handle = current.Current.NativeWindowHandle;
            }

            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return handle;
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

    private static bool EnsureComposerFocus(AutomationElement element, nint hostWindow)
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
        if (foreground != hostWindow && foregroundProcessId != Environment.ProcessId)
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

    private sealed class NativeEditTarget(ComposerSnapshot snapshot, AutomationElement element)
        : IComposerEditTarget
    {
        public bool IsCurrentEditor => snapshot.HostWindow != 0
            && GetForegroundWindow() == snapshot.HostWindow
            && IsFocusedElementOrAncestor(element)
            && element.Current.ProcessId == snapshot.ProcessId
            && !element.Current.IsOffscreen && element.Current.IsEnabled
            && IsSameRuntimeId(element.GetRuntimeId(), snapshot.RuntimeId);

        public string? Read() => ReadText(element);

        public bool CanReplace(int start, string expected)
        {
            if (snapshot.ReadMethod == ComposerReadMethod.ValuePattern)
            {
                return start == 0 && Read() == expected
                    && element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                    && !((ValuePattern)pattern).Current.IsReadOnly;
            }

            return CreateVerifiedRange(start, expected) is not null;
        }

        public bool Replace(int start, string expected, string replacement)
        {
            if (!IsCurrentEditor)
            {
                return false;
            }

            var before = Read();
            if (before is null || start < 0 || start > before.Length || expected.Length > before.Length - start
                || !before.AsSpan(start, expected.Length).SequenceEqual(expected))
            {
                return false;
            }

            var after = before.Remove(start, expected.Length).Insert(start, replacement);
            if (snapshot.ReadMethod == ComposerReadMethod.ValuePattern)
            {
                if (!CanReplace(start, expected) || !IsCurrentEditor || Read() != before)
                {
                    return false;
                }

                ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(replacement);
            }
            else
            {
                var range = CreateVerifiedRange(start, expected);
                if (range is null || !IsCurrentEditor || Read() != before)
                {
                    return false;
                }

                range.Select();
                var textPattern = (TextPattern)element.GetCurrentPattern(TextPattern.Pattern);
                var selection = textPattern.GetSelection();
                if (selection.Length != 1 || selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start,
                        range, TextPatternRangeEndpoint.Start) != 0
                    || selection[0].CompareEndpoints(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.End) != 0
                    || selection[0].GetText(-1) != expected || !IsCurrentEditor || Read() != before)
                {
                    return false;
                }

                if (!UnicodeInput.ReplaceSelection(replacement))
                {
                    return false;
                }
            }

            return WaitForText(element, after);
        }

        private TextPatternRange? CreateVerifiedRange(int start, string expected)
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                return null;
            }

            var document = ((TextPattern)pattern).DocumentRange;
            var full = document.GetText(-1);
            if (start < 0 || start > full.Length || expected.Length > full.Length - start
                || !full.AsSpan(start, expected.Length).SequenceEqual(expected))
            {
                return null;
            }

            var before = PrefixRange(document, full, start);
            var through = PrefixRange(document, full, start + expected.Length);
            if (before is null || through is null)
            {
                return null;
            }

            through.MoveEndpointByRange(TextPatternRangeEndpoint.Start, before, TextPatternRangeEndpoint.End);
            return through.GetText(-1) == expected ? through : null;
        }

        private static TextPatternRange? PrefixRange(TextPatternRange document, string full, int utf16Length)
        {
            var low = 0;
            var high = utf16Length;
            while (low <= high)
            {
                var units = low + (high - low) / 2;
                var prefix = document.Clone();
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, document, TextPatternRangeEndpoint.Start);
                _ = prefix.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, units);
                var text = prefix.GetText(-1);
                if (text.Length == utf16Length)
                {
                    return text.AsSpan().SequenceEqual(full.AsSpan(0, utf16Length)) ? prefix : null;
                }

                if (text.Length < utf16Length)
                {
                    low = units + 1;
                }
                else
                {
                    high = units - 1;
                }
            }

            return null;
        }
    }

    private static bool IsSameRuntimeId(IReadOnlyList<int> left, IReadOnlyList<int> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    private static class UnicodeInput
    {
        private const ushort VirtualKeyBack = 0x08;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventUnicode = 0x0004;
        private const int InputKeyboard = 1;
        public static int StructSize => Marshal.SizeOf<INPUT>();

        public static bool ReplaceSelection(string text)
        {
            if (text.Any(character => char.IsControl(character) || character is '\ufffc' or '\u2028' or '\u2029'))
            {
                return false;
            }

            if (text.Length == 0)
            {
                var deletion = new[] { Key(VirtualKeyBack, 0), Key(VirtualKeyBack, KeyEventKeyUp) };
                return SendInput((uint)deletion.Length, deletion, StructSize) == deletion.Length;
            }

            var inputs = new INPUT[text.Length * 2];
            for (var index = 0; index < text.Length; index++)
            {
                inputs[index * 2] = UnicodeKey(text[index], 0);
                inputs[index * 2 + 1] = UnicodeKey(text[index], KeyEventKeyUp);
            }

            return SendInput((uint)inputs.Length, inputs, StructSize) == inputs.Length;
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
