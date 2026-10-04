using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Windows;

public sealed class CodexComposerAccessor : IComposerAccessor
{
    private ComposerSnapshot? _lastAppliedEditor;
    private ComposerEditPlan? _lastAppliedPlan;
    public string LastWriteStatus { get; private set; } = "not_attempted";
    public ComposerWriteResult? LastWriteResult { get; private set; }
    public string LastCaptureStatus { get; private set; } = "not_attempted";
    public ComposerHost? LastCaptureHost { get; private set; }
    public string? LastFieldCategory { get; private set; }
    internal static int NativeInputSize => UnicodeInput.StructSize;

    private readonly ComposerLayoutCache _layoutCache = new();
    private long _layoutTimestamp;
    internal long CaptureCount { get; private set; }
    internal long LayoutCaptureCount { get; private set; }

    public void InvalidateLayout() => _layoutCache.Clear();

    public ComposerSnapshot? TryCaptureFocusedComposer() => CaptureFocusedComposer(false);
    public ComposerSnapshot? TryPollFocusedComposer() => CaptureFocusedComposer(true);

    private ComposerSnapshot? CaptureFocusedComposer(bool reuseLayout)
    {
        LastCaptureStatus = "unavailable";
        LastCaptureHost = null;
        LastFieldCategory = null;
        try
        {
            var foreground = GetForegroundWindow();
            _ = GetWindowThreadProcessId(foreground, out var processId);
            if (foreground == 0 || (processId != Environment.ProcessId && TryGetComposerHost(processId) is null))
            {
                LastCaptureStatus = "other_host";
                return null;
            }
            CaptureCount++;
            LastCaptureHost = TryGetComposerHost(processId);
            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                var editable = FindEditableAncestor(focused);
                var focusedHost = editable is null ? null : TryGetComposerHost(editable);
                if (editable is not null && focusedHost is not null && !IsPasswordField(editable)
                    && IsEligibleField(editable, focusedHost.Value))
                {
                    return CaptureSnapshot(editable, focusedHost.Value, reuseLayout);
                }
                // A focused search, rename field or terminal must not redirect to a different composer.
                if (editable is not null)
                {
                    LastCaptureStatus = "focused_non_composer";
                    return null;
                }
            }

            return TryCaptureActiveWindowComposer(focused, reuseLayout);
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
        finally
        {
            if (LastCaptureStatus != "captured") InvalidateLayout();
        }
    }

    private ComposerSnapshot? TryCaptureActiveWindowComposer(AutomationElement? focused, bool reuseLayout)
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
            _ = GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcessId);
            LastCaptureStatus = root.Current.ProcessId == Environment.ProcessId
                ? (foregroundProcessId == Environment.ProcessId ? "own_overlay_foreground" : "own_overlay_uia_root")
                : "other_host";
            return null;
        }

        var focusableElements = root.FindAll(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true),
                new PropertyCondition(AutomationElement.IsEnabledProperty, true),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false),
                new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox))));
        AutomationElement? candidate = null;
        foreach (AutomationElement element in focusableElements)
        {
            if (!IsEligibleField(element, host.Value)
                || IsPasswordField(element)
                || !element.Current.IsEnabled
                || element.Current.IsOffscreen
                || (!IsComposerShapeForHost(element, host.Value) && !element.Current.HasKeyboardFocus))
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
                LastCaptureStatus = "ambiguous_editors";
                return null;
            }

            candidate = element;
        }

        if (candidate is null)
        {
            LastCaptureStatus = "no_matching_editor";
            return null;
        }
        return CaptureSnapshot(candidate, host.Value, reuseLayout);
    }

    private ComposerSnapshot? CaptureSnapshot(AutomationElement element, ComposerHost host, bool reuseLayout)
    {
        LastCaptureHost = host;
        LastFieldCategory = IsComposerShapeForHost(element, host) ? "chat" : "prose";
        var snapshot = CreateSnapshot(element, host, reuseLayout);
        if (LastFieldCategory == "chat" && snapshot is not null
            && NaturalLanguageField.IsAmbiguousComposerHint(host, element.Current.Name, snapshot.Text))
        {
            LastCaptureStatus = "ambiguous_placeholder";
            return null;
        }
        LastCaptureStatus = snapshot is null ? "editor_unreadable" : "captured";
        return snapshot;
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
                && HasClassToken(classes, "ProseMirror"),
            ComposerHost.Antigravity => type == ControlType.ComboBox
                && HasClassToken(classes, "cursor-text")
                && HasClassToken(classes, "overflow-y-auto"),
            ComposerHost.Hermes => type == ControlType.Edit
                && HasClassToken(classes, "overflow-y-auto")
                && (HasClassToken(classes, "cursor-text")
                    || HasClassToken(classes, "ui-prompt-input-editor__input")),
            _ => false
        };
    }

    private static bool IsComposerShapeForHost(AutomationElement element, ComposerHost host) =>
        IsComposerShapeForHost(
            element.Current.ControlType,
            element.Current.ClassName,
            host);

    private static bool IsEligibleField(AutomationElement element, ComposerHost host)
    {
        if (!element.Current.IsEnabled || element.Current.IsOffscreen || IsPasswordField(element)) return false;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
            && ((ValuePattern)value).Current.IsReadOnly) return false;
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern)
            && ((TextPattern)textPattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is true) return false;
        if (IsComposerShapeForHost(element, host)) return true;
        if (host == ComposerHost.Antigravity || element.Current.ControlType != ControlType.Edit) return false;
        var label = element.Current.LabeledBy?.Current.Name;
        if (NaturalLanguageField.IsSupported(host, className: element.Current.ClassName,
            automationId: element.Current.AutomationId, name: element.Current.Name, associatedLabel: label)) return true;
        // Some host forms render a static caption without an HTML label association.
        // Accept only an exact known caption immediately above a single-editor group.
        var bounds = element.Current.BoundingRectangle;
        var scale = GetDpiForWindow(FindHostWindow(element)) / 96d;
        if (scale <= 0) return false;
        var parent = TreeWalker.ControlViewWalker.GetParent(element);
        for (var depth = 0; depth < 3 && parent is not null; depth++)
        {
            if (parent.Current.ControlType == ControlType.Document || parent.Current.ControlType == ControlType.Window) break;
            var edits = parent.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edits.Count > 1) break;
            var captions = FindVisibleControls(parent,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            foreach (AutomationElement caption in captions)
            {
                var captionBounds = caption.Cached.BoundingRectangle;
                if (captionBounds.IsEmpty || captionBounds.Bottom > bounds.Top
                    || bounds.Top - captionBounds.Bottom > 24 * scale
                    || captionBounds.Right <= bounds.Left || captionBounds.Left >= bounds.Right) continue;
                if (NaturalLanguageField.IsSupported(host, className: element.Current.ClassName,
                    automationId: element.Current.AutomationId, name: element.Current.Name,
                    associatedLabel: caption.Current.Name)) return true;
            }
            parent = TreeWalker.ControlViewWalker.GetParent(parent);
        }
        return false;
    }

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
                || TryGetComposerHost(element) != snapshot.Host || !IsEligibleField(element, snapshot.Host))
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

            var plan = _lastAppliedPlan is { } previous
                && _lastAppliedEditor is { } previousEditor && previousEditor.SameEditor(current)
                && previous.Corrected == expectedText && previous.Original == replacement
                    ? previous.Reverse()
                    : ComposerEditPlan.Create(expectedText, replacement);
            if (plan is null)
            {
                return Failed("unsafe_edit_plan");
            }

            if (!EnsureComposerFocus(element, snapshot.HostWindow))
            {
                return Failed("focus_changed_" + FocusDiagnostic(element, snapshot.HostWindow));
            }

            var target = new NativeEditTarget(snapshot, element);
            if (snapshot.ReadMethod == ComposerReadMethod.ValuePattern)
            {
                plan = new ComposerEditPlan(expectedText, replacement,
                    expectedText == replacement ? [] : [new ComposerTextEdit(0, expectedText, replacement)]);
            }

            LastWriteResult = ComposerWriteTransaction.Apply(plan, target);
            if (LastWriteResult.State != ComposerWriteState.Applied)
            {
                LastWriteResult = LastWriteResult with { Status = LastWriteResult.Status + "_" + target.LastStatus };
            }
            LastWriteStatus = LastWriteResult.Status;
            if (LastWriteResult.State == ComposerWriteState.Applied)
            {
                _lastAppliedEditor = current;
                _lastAppliedPlan = plan;
            }
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
        if (!element.Current.IsEnabled || !element.Current.IsKeyboardFocusable)
        {
            return false;
        }

        var hasValuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern);
        var hasWritableValue = hasValuePattern && !((ValuePattern)valuePattern).Current.IsReadOnly;
        if (!IsTextInputForFocusGuard(type, className, automationId, hasWritableValue))
        {
            return false;
        }

        return type == ControlType.Edit || hasWritableValue || hasValuePattern
            || element.TryGetCurrentPattern(TextPattern.Pattern, out _);
    }

    internal static bool IsTextInputForFocusGuard(
        ControlType type, string? className, string? automationId, bool hasWritableValue) =>
        !(type == ControlType.Document
            && string.Equals(automationId, "RootWebArea", StringComparison.OrdinalIgnoreCase))
        && (type == ControlType.Edit || hasWritableValue
            || IsSupportedComposerShape(type, className, automationId));

    internal static bool IsSupportedComposerShape(ControlType type, string? className, string? automationId)
    {
        var isProseMirror = (type == ControlType.Group || type == ControlType.Edit || type == ControlType.Document)
            && HasClassToken(className, "ProseMirror");
        var isRootWebArea = type == ControlType.Document
            && string.Equals(automationId, "RootWebArea", StringComparison.OrdinalIgnoreCase);
        var isTokenizedComposer = (type == ControlType.ComboBox || type == ControlType.Edit)
            && HasClassToken(className, "cursor-text")
            && HasClassToken(className, "overflow-y-auto");
        return !isRootWebArea
            && (isProseMirror || isTokenizedComposer);
    }

    private static bool HasClassToken(string? className, string expected) =>
        (className ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(token => string.Equals(token, expected, StringComparison.OrdinalIgnoreCase));

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

    private static ComposerHost? TryGetComposerHost(AutomationElement element) => TryGetComposerHost(element.Current.ProcessId);

    private static ComposerHost? TryGetComposerHost(int processId) => ComposerHostProcess.Identify(processId);

    private ComposerSnapshot? CreateSnapshot(AutomationElement element, ComposerHost host, bool reuseLayout = false)
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

        var hostWindow = FindHostWindow(element);
        var current = new ComposerSnapshot(element, element.GetRuntimeId(), text, editorBounds,
            element.Current.ProcessId, Host: host, HostWindow: hostWindow, EditorBounds: editorBounds, ReadMethod: readMethod,
            FieldCategory: IsComposerShapeForHost(element, host) ? "chat" : "prose");
        var dpi = hostWindow == 0 ? 0 : GetDpiForWindow(hostWindow);
        // Nearby controls can move without changing the editor or producing a WinEvent.
        // Every periodic poll reads their geometry; rapid status-only refreshes may reuse it.
        if (reuseLayout && Stopwatch.GetElapsedTime(_layoutTimestamp).TotalMilliseconds < 50
            && _layoutCache.TryApply(current, dpi) is { } cached)
            return cached;

        LayoutCaptureCount++;
        var surfaceBounds = editorBounds;
        var toolbar = new ToolbarContext(null, [editorBounds]);
        var layoutResolved = true;
        try
        {
            if (current.FieldCategory == "chat")
            {
                surfaceBounds = FindComposerSurfaceBounds(element, editorBounds);
                toolbar = FindToolbarContext(element, surfaceBounds, host);
                if (surfaceBounds == editorBounds)
                {
                    var association = FindAdjacentToolbar(element, editorBounds, hostWindow);
                    if (association is not null)
                    {
                        surfaceBounds = association.SurfaceBounds;
                        Rect? anchor = null;
                        foreach (var button in association.RowButtons)
                        {
                            if ((host != ComposerHost.Codex || IsCodexModelAnchor(button))
                                && IsBetterToolbarAnchor(button, anchor, host))
                            {
                                anchor = button;
                            }
                        }

                        toolbar = new ToolbarContext(anchor, association.OccupiedBounds);
                    }
                    else
                    {
                        layoutResolved = false;
                    }
                }

                if (toolbar.OccupiedBounds is null)
                {
                    layoutResolved = false;
                    surfaceBounds = editorBounds;
                    toolbar = new ToolbarContext(null, [editorBounds]);
                }
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException
            or ArgumentException or COMException)
        {
            // Layoutknoten können verschwinden, obwohl der separat geprüfte Editor weiterhin gültig ist.
            layoutResolved = false;
            surfaceBounds = editorBounds;
            toolbar = new ToolbarContext(null, [editorBounds]);
        }

        Rect? placementBounds = null;
        try
        {
            var nearby = CapturePlacementContext(element, editorBounds, hostWindow, dpi);
            placementBounds = nearby.Bounds;
            if (nearby.Obstacles is not null)
                toolbar = toolbar with { OccupiedBounds = (toolbar.OccupiedBounds ?? []).Concat(nearby.Obstacles).Distinct().ToArray() };
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException
            or ArgumentException or COMException)
        {
            layoutResolved = false;
        }
        var result = current with
        {
            Bounds = surfaceBounds,
            RightControlBounds = toolbar.RightControlBounds,
            OccupiedBounds = toolbar.OccupiedBounds,
            PlacementBounds = placementBounds,
            EditorTextBounds = host == ComposerHost.Codex
                && TryGetVisibleTextBounds(element, null) is { Count: > 0 } textBounds
                    ? textBounds : null
        };
        if (layoutResolved)
        {
            _layoutCache.Store(result, dpi);
            _layoutTimestamp = Stopwatch.GetTimestamp();
        }
        else _layoutCache.Clear();
        return result;
    }

    private static (Rect? Bounds, IReadOnlyList<Rect>? Obstacles) CapturePlacementContext(
        AutomationElement editor, Rect editorBounds, nint window, uint dpi)
    {
        var document = FindDocumentRoot(editor);
        if (document is null || window == 0 || dpi == 0) return default;
        var windowRoot = AutomationElement.FromHandle(window);
        var available = Rect.Intersect(document.Current.BoundingRectangle, windowRoot.Current.BoundingRectangle);
        var scope = document;
        var parent = TreeWalker.ControlViewWalker.GetParent(editor);
        for (var depth = 0; depth < 20 && parent is not null; depth++)
        {
            if (IsSameRuntimeId(parent.GetRuntimeId(), document.GetRuntimeId())) break;
            if (parent.Current.ControlType == ControlType.Window)
            {
                available.Intersect(parent.Current.BoundingRectangle);
                scope = parent;
                break;
            }
            parent = TreeWalker.ControlViewWalker.GetParent(parent);
        }
        if (available.IsEmpty || !available.Contains(editorBounds)) return default;
        var nearby = editorBounds;
        nearby.Inflate(36 * dpi / 96d, 36 * dpi / 96d);
        nearby.Intersect(available);
        var ancestors = GetAncestorRuntimeIds(editor);
        var obstacles = new List<Rect> { editorBounds };
        var controls = FindVisibleControls(scope, new OrCondition(
            new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image)));
        foreach (AutomationElement control in controls)
        {
            var bounds = control.Cached.BoundingRectangle;
            if (bounds.IsEmpty || !nearby.IntersectsWith(bounds)
                || !IsToolbarObstacle(control.Cached.ControlType, control.Cached.ClassName, control.Cached.AutomationId,
                    ancestors.Contains(string.Join(",", control.GetRuntimeId())))) continue;
            var visibleText = control.Cached.ControlType == ControlType.Text
                ? TryGetVisibleTextBounds(control, document) : null;
            obstacles.AddRange(visibleText ?? [bounds]);
        }
        return (available, obstacles);
    }

    private static ComposerToolbarAssociation? FindAdjacentToolbar(AutomationElement composer, Rect editor, nint hostWindow)
    {
        var document = FindDocumentRoot(composer);
        if (document is null || hostWindow == 0)
        {
            return null;
        }

        var dpi = GetDpiForWindow(hostWindow);
        if (dpi == 0) return null;
        var nearby = editor;
        nearby.Inflate(200 * dpi / 96d, 100 * dpi / 96d);

        var scope = FindToolbarScope(composer, document);
        var controls = FindVisibleControls(scope, new OrCondition(
            new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
        var bounds = new List<ComposerControlBounds>();
        var ancestors = GetAncestorRuntimeIds(composer);
        foreach (AutomationElement control in controls)
        {
            var type = control.Cached.ControlType;
            if (!IsToolbarObstacle(type, control.Cached.ClassName, control.Cached.AutomationId,
                ancestors.Contains(string.Join(",", control.GetRuntimeId())))) continue;
            var aggregate = control.Cached.BoundingRectangle;
            var isEditor = type == ControlType.Edit || IsSupportedComposerShape(type, control.Cached.ClassName, control.Cached.AutomationId);
            var visibleText = type == ControlType.Text && !editor.Contains(aggregate) && aggregate.IntersectsWith(nearby)
                ? TryGetVisibleTextBounds(control, document) : null;
            foreach (var rectangle in visibleText ?? [aggregate])
                bounds.Add(new ComposerControlBounds(rectangle, type == ControlType.Button, isEditor));
        }

        return ComposerToolbarGeometry.TryAssociate(editor, scope.Current.BoundingRectangle, bounds, dpi / 96d);
    }

    private static AutomationElement FindToolbarScope(AutomationElement composer, AutomationElement document)
    {
        var editor = composer.Current.BoundingRectangle;
        var current = TreeWalker.ControlViewWalker.GetParent(composer);
        for (var depth = 0; depth < 20 && current is not null; depth++)
        {
            if (IsSameRuntimeId(current.GetRuntimeId(), document.GetRuntimeId())) break;
            var candidateBounds = current.Current.BoundingRectangle;
            if (IsComposerSurfaceCandidate(editor, candidateBounds)
                && HasAssociatedControls(current, candidateBounds, editor))
                return current;
            if (current.Current.ControlType == ControlType.Window && current.Current.BoundingRectangle.Contains(editor))
                return current;
            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return document;
    }

    internal static IReadOnlyList<Rect>? TryGetVisibleTextBounds(AutomationElement element, AutomationElement? document)
    {
        try
        {
            TextPatternRange? range = null;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var ownPattern))
                range = ((TextPattern)ownPattern).DocumentRange;
            else if (document is not null && document.TryGetCurrentPattern(TextPattern.Pattern, out var rootPattern))
                range = ((TextPattern)rootPattern).RangeFromChild(element);
            if (range is null || range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
                return null;
            var rectangles = range.GetBoundingRectangles();
            return rectangles.All(rectangle => !rectangle.IsEmpty && rectangle.Width > 0 && rectangle.Height > 0
                && double.IsFinite(rectangle.Left) && double.IsFinite(rectangle.Top)
                && double.IsFinite(rectangle.Right) && double.IsFinite(rectangle.Bottom)) ? rectangles : null;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException
            or ArgumentException or COMException)
        {
            return null;
        }
    }

    internal static bool IsToolbarObstacle(ControlType type, string? className, string? automationId, bool isEditorAncestor = false)
    {
        if (IsSupportedComposerShape(type, className, automationId)) return true;
        if (type == ControlType.Document) return false;
        return !isEditorAncestor || (type != ControlType.Group && type != ControlType.Pane
            && type != ControlType.Window && type != ControlType.Custom);
    }

    private static HashSet<string> GetAncestorRuntimeIds(AutomationElement composer)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var current = TreeWalker.ControlViewWalker.GetParent(composer);
        for (var depth = 0; depth < 30 && current is not null; depth++)
        {
            result.Add(string.Join(",", current.GetRuntimeId()));
            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return result;
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

    private static AutomationElementCollection FindVisibleControls(AutomationElement root, System.Windows.Automation.Condition condition)
    {
        var request = new CacheRequest { TreeScope = TreeScope.Element };
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.ClassNameProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        request.Add(AutomationElement.IsOffscreenProperty);
        using (request.Activate())
        {
            return root.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false), condition));
        }
    }

    private static bool HasAssociatedControls(AutomationElement surface, Rect bounds, Rect editor)
    {
        var buttons = FindVisibleControls(surface,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            var controlBounds = button.Cached.BoundingRectangle;
            if (!controlBounds.IsEmpty
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

            var controls = FindVisibleControls(searchRoot,
                new OrCondition(
                    new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)));
            var minimumLeft = composerBounds.Left + composerBounds.Width * (
                host == ComposerHost.Codex ? 0.35 : 0.55);
            var maximumTopDistance = Math.Max(48, composerBounds.Height);
            Rect? best = null;
            var occupied = new List<Rect> { composer.Current.BoundingRectangle };
            var editorBounds = composer.Current.BoundingRectangle;
            var document = FindDocumentRoot(composer);
            var ancestors = GetAncestorRuntimeIds(composer);
            foreach (AutomationElement button in controls)
            {
                if (!IsToolbarObstacle(button.Cached.ControlType, button.Cached.ClassName, button.Cached.AutomationId,
                    ancestors.Contains(string.Join(",", button.GetRuntimeId())))) continue;
                var bounds = button.Cached.BoundingRectangle;
                if (!bounds.IsEmpty && !button.Cached.IsOffscreen && bounds.IntersectsWith(composerBounds))
                {
                    var visibleText = button.Cached.ControlType == ControlType.Text && !editorBounds.Contains(bounds)
                        ? TryGetVisibleTextBounds(button, document) : null;
                    occupied.AddRange(visibleText ?? [bounds]);
                }

                if (bounds.IsEmpty
                    || button.Cached.ControlType != ControlType.Button
                    || button.Cached.IsOffscreen
                    || bounds.Width < 24
                    || bounds.Left < minimumLeft
                    || bounds.Left >= composerBounds.Right
                    || bounds.Bottom < composerBounds.Top - 12
                    || bounds.Top > composerBounds.Bottom + maximumTopDistance)
                {
                    continue;
                }

                var isAnchor = host != ComposerHost.Codex
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
            || IsComposerShapeForHost(element.Current.ControlType, element.Current.ClassName, ComposerHost.Antigravity)
            || IsComposerShapeForHost(element.Current.ControlType, element.Current.ClassName, ComposerHost.Hermes);
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

    private static string FocusDiagnostic(AutomationElement element, nint hostWindow)
    {
        var focused = AutomationElement.FocusedElement;
        return $"host_{(GetForegroundWindow() == hostWindow ? "same" : "changed")}"
            + $"_focus_{(IsFocusedElementOrAncestor(element) ? "same" : "changed")}"
            + $"_type_{focused?.Current.ControlType.Id ?? 0}";
    }

    private sealed class NativeEditTarget(ComposerSnapshot snapshot, AutomationElement element)
        : IComposerEditTarget
    {
        public string LastStatus { get; private set; } = "not_attempted";
        private bool _failed;

        private bool Fail(string status)
        {
            if (!_failed) LastStatus = status;
            _failed = true;
            return false;
        }

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
                return Fail("invalid_identity");
            }

            var before = Read();
            if (before is null || start < 0 || start > before.Length || expected.Length > before.Length - start
                || !before.AsSpan(start, expected.Length).SequenceEqual(expected))
            {
                return Fail("before_mismatch");
            }

            var after = before.Remove(start, expected.Length).Insert(start, replacement);
            if (snapshot.ReadMethod == ComposerReadMethod.ValuePattern)
            {
                if (!CanReplace(start, expected)) return Fail("range_unavailable");
                if (!IsCurrentEditor) return Fail("invalid_identity");
                if (Read() != before) return Fail("before_mismatch");

                ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(replacement);
            }
            else
            {
                var insertionAnchor = expected.Length == 0 ? ComposerTextRangeMapping.InsertionAnchor(before, start) : null;
                if (expected.Length == 0 && insertionAnchor is null) return Fail("insertion_anchor_unavailable");
                var selectionStart = insertionAnchor?.Start ?? start;
                var anchorText = insertionAnchor is { } anchor ? before.Substring(anchor.Start, anchor.Length) : expected;
                var range = CreateVerifiedRange(selectionStart, anchorText);
                if (range is null) return Fail("range_unavailable");
                if (!IsCurrentEditor) return Fail("invalid_identity");
                if (Read() != before) return Fail("before_mismatch");

                // Nur den Anfang nativ setzen: Chromium kann ein UIA-Bereichsende hinter
                // einen echten Absatzumbruch verschieben. Die Auswahl wird per Tastatur aufgebaut.
                var caret = range.Clone();
                caret.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
                caret.Select();
                var textPattern = (TextPattern)element.GetCurrentPattern(TextPattern.Pattern);
                var confirmation = ComposerSelectionVerification.WaitForExact(() => CheckSelection("", selectionStart));
                if (confirmation != ComposerSelectionCheck.Exact)
                    return Fail(ComposerSelectionVerification.Status(confirmation));
                if (!IsCurrentEditor) return Fail("invalid_identity_after_selection");
                if (Read() != before) return Fail("before_mismatch_after_selection");
                if (expected.Length == 0)
                {
                    if (selectionStart < start && !UnicodeInput.MoveFollowingText(anchorText))
                        return Fail("caret_sendinput_failed");
                }
                else if (!UnicodeInput.SelectFollowingText(expected)) return Fail("selection_sendinput_failed");
                confirmation = ComposerSelectionVerification.WaitForExact(() => CheckSelection(expected, start));
                if (confirmation != ComposerSelectionCheck.Exact)
                    return Fail(ComposerSelectionVerification.Status(confirmation));
                if (!IsCurrentEditor) return Fail("invalid_identity_after_selection");
                if (Read() != before) return Fail("before_mismatch_after_selection");

                if (!UnicodeInput.ReplaceSelection(replacement))
                {
                    return Fail("sendinput_failed");
                }

                ComposerSelectionCheck CheckSelection(string selectedText, int selectedStart)
                {
                    if (!IsCurrentEditor) return ComposerSelectionCheck.EditorChanged;
                    if (Read() != before) return ComposerSelectionCheck.SourceChanged;
                    var selection = textPattern.GetSelection();
                    var result = ComposerSelectionCheck.Exact;
                    if (selection.Length != 1) result = ComposerSelectionCheck.CountMismatch;
                    else
                    {
                        // Chromium kann dieselbe Auswahl mit anderen AX-Endpunktankern zurückgeben.
                        // Inhalt und vollständige Umgebung müssen dennoch ordinal exakt übereinstimmen.
                        var document = textPattern.DocumentRange;
                        var prefix = document.Clone();
                        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,
                            selection[0], TextPatternRangeEndpoint.Start);
                        var through = document.Clone();
                        through.MoveEndpointByRange(TextPatternRangeEndpoint.End,
                            selection[0], TextPatternRangeEndpoint.End);
                        var suffix = document.Clone();
                        suffix.MoveEndpointByRange(TextPatternRangeEndpoint.Start,
                            selection[0], TextPatternRangeEndpoint.End);
                        if (!ComposerTextRangeMapping.MatchesSelection(before, selectedStart, selectedText,
                            document.GetText(-1), prefix.GetText(-1), through.GetText(-1),
                            selection[0].GetText(-1), suffix.GetText(-1)))
                            result = ComposerSelectionCheck.TextMismatch;
                    }
                    if (!IsCurrentEditor) return ComposerSelectionCheck.EditorChanged;
                    if (Read() != before) return ComposerSelectionCheck.SourceChanged;
                    return result;
                }
            }

            var readback = ComposerWriteVerification.WaitForExact(after, Read, () => IsCurrentEditor);
            if (readback != ComposerWriteCheck.Exact)
                return Fail(readback == ComposerWriteCheck.EditorChanged
                    ? "readback_editor_changed_" + FocusDiagnostic(element, snapshot.HostWindow) : "readback_mismatch");
            if (!_failed) LastStatus = "ok";
            return true;
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

            if (expected.Length > 0) return FindVerifiedTextRange(document, full, start, expected);

            var anchor = ComposerTextRangeMapping.InsertionAnchor(full, start);
            if (anchor is null) return null;
            var caret = anchor.Value.Length == 0 ? document.Clone()
                : FindVerifiedTextRange(document, full, anchor.Value.Start,
                    full.Substring(anchor.Value.Start, anchor.Value.Length));
            if (caret is null) return null;

            if (start == 0)
                caret.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            else
                caret.MoveEndpointByRange(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End);

            var prefix = document.Clone();
            prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            return caret.CompareEndpoints(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End) == 0
                && ComposerTextRangeMapping.Matches(full, start, expected, prefix.GetText(-1), caret.GetText(-1))
                ? caret : null;
        }

        private TextPatternRange? FindVerifiedTextRange(
            TextPatternRange document, string full, int start, string expected)
        {
            var remaining = document.Clone();
            var textPattern = (TextPattern)element.GetCurrentPattern(TextPattern.Pattern);
            var startAnchored = false;
            var endAnchored = false;
            // FindText kann generierte Absatztrenner vor einem Treffer falsch mitzählen.
            // Beide Suchgrenzen auf die Zieltextknoten begrenzen, ohne Offsets zu korrigieren.
            foreach (AutomationElement textElement in element.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
            {
                var textRange = textPattern.RangeFromChild(textElement);
                var prefix = document.Clone();
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, textRange, TextPatternRangeEndpoint.Start);
                var prefixText = prefix.GetText(-1);
                var rangeText = textRange.GetText(-1);
                if (!startAnchored && ComposerTextRangeMapping.IsSearchAnchor(full, start, prefixText, rangeText))
                {
                    remaining.MoveEndpointByRange(TextPatternRangeEndpoint.Start, textRange, TextPatternRangeEndpoint.Start);
                    startAnchored = true;
                }
                if (!endAnchored && ComposerTextRangeMapping.IsSearchAnchor(full, start + expected.Length - 1, prefixText, rangeText))
                {
                    remaining.MoveEndpointByRange(TextPatternRangeEndpoint.End, textRange, TextPatternRangeEndpoint.End);
                    endAnchored = true;
                }
                if (startAnchored && endAnchored) break;
            }
            var previousPrefixLength = -1;
            for (var attempt = 0; attempt <= full.Length; attempt++)
            {
                var found = remaining.FindText(expected, backward: false, ignoreCase: false);
                if (found is null
                    || found.CompareEndpoints(TextPatternRangeEndpoint.Start, remaining, TextPatternRangeEndpoint.Start) < 0
                    || found.CompareEndpoints(TextPatternRangeEndpoint.End, document, TextPatternRangeEndpoint.End) > 0
                    || found.CompareEndpoints(TextPatternRangeEndpoint.Start, found, TextPatternRangeEndpoint.End) >= 0)
                    return null;

                var prefix = document.Clone();
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, found, TextPatternRangeEndpoint.Start);
                var prefixText = prefix.GetText(-1);
                if (prefixText.Length <= previousPrefixLength || prefixText.Length > start
                    || !ComposerTextRangeMapping.Matches(full, prefixText.Length, expected, prefixText, found.GetText(-1)))
                    return null;

                if (prefixText.Length == start)
                {
                    prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, found, TextPatternRangeEndpoint.End);
                    return prefix.GetText(-1).AsSpan().SequenceEqual(full.AsSpan(0, start + expected.Length))
                        ? found : null;
                }

                previousPrefixLength = prefixText.Length;
                // Ab Trefferstart fortsetzen, damit überlappende Wiederholungen nicht übersprungen werden.
                var next = found.Clone();
                next.MoveEndpointByRange(TextPatternRangeEndpoint.End, found, TextPatternRangeEndpoint.Start);
                if (next.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) != 1
                    || next.CompareEndpoints(TextPatternRangeEndpoint.End, found, TextPatternRangeEndpoint.Start) <= 0
                    || next.CompareEndpoints(TextPatternRangeEndpoint.End, remaining, TextPatternRangeEndpoint.Start) <= 0
                    || next.CompareEndpoints(TextPatternRangeEndpoint.End, document, TextPatternRangeEndpoint.End) > 0)
                    return null;
                remaining.MoveEndpointByRange(TextPatternRangeEndpoint.Start, next, TextPatternRangeEndpoint.End);
            }

            return null;
        }
    }

    private static bool IsSameRuntimeId(IReadOnlyList<int> left, IReadOnlyList<int> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    internal static class UnicodeInput
    {
        private const ushort VirtualKeyBack = 0x08;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventUnicode = 0x0004;
        private const int InputKeyboard = 1;
        public static int StructSize => Marshal.SizeOf<INPUT>();

        public static bool MoveFollowingText(string text)
        {
            var keys = CaretKeys(text);
            if (keys.Length == 0 || !ModifiersAreReleased()) return false;
            var inputs = keys.Select(key => Key(key.Key, key.Flags)).ToArray();
            var sent = SendInput((uint)inputs.Length, inputs, StructSize);
            if (sent == inputs.Length) return true;
            if (sent % 2 != 0)
            {
                var release = new[] { Key(0x27, 0x0001 | KeyEventKeyUp) };
                _ = SendInput(1, release, StructSize);
            }
            return false;
        }

        internal static (ushort Key, uint Flags)[] CaretKeys(string text)
        {
            var selection = SelectionKeys(text);
            return selection.Length == 0 ? [] : selection[1..^1];
        }

        public static bool SelectFollowingText(string text)
        {
            var keys = SelectionKeys(text);
            if (keys.Length == 0) return false;
            var inputs = keys.Select(key => Key(key.Key, key.Flags)).ToArray();
            if (!ModifiersAreReleased()) return false;
            var sent = SendInput((uint)inputs.Length, inputs, StructSize);
            if (sent == inputs.Length) return true;
            var release = SelectionReleaseKeys(sent, inputs.Length).Select(key => Key(key.Key, key.Flags)).ToArray();
            if (release.Length > 0) _ = SendInput((uint)release.Length, release, StructSize);
            return false;
        }

        internal static (ushort Key, uint Flags)[] SelectionKeys(string text)
        {
            if (text.Length == 0 || text.Any(character => char.IsControl(character)
                || character is '\ufffc' or '\u2028' or '\u2029')) return [];
            var count = StringInfo.ParseCombiningCharacters(text).Length;
            var keys = new (ushort Key, uint Flags)[count * 2 + 2];
            keys[0] = (0x10, 0); // Shift
            for (var index = 0; index < count; index++)
            {
                keys[index * 2 + 1] = (0x27, 0x0001); // Right, extended key
                keys[index * 2 + 2] = (0x27, 0x0001 | KeyEventKeyUp);
            }
            keys[^1] = (0x10, KeyEventKeyUp);
            return keys;
        }

        internal static (ushort Key, uint Flags)[] SelectionReleaseKeys(uint sent, int planned)
        {
            if (sent == 0 || sent >= planned) return [];
            // Nur eigene angenommene Key-down-Ereignisse auflösen, niemals eine zweite Auswahl senden.
            return sent > 1 && sent % 2 == 0
                ? [(0x27, 0x0001 | KeyEventKeyUp), (0x10, KeyEventKeyUp)]
                : [(0x10, KeyEventKeyUp)];
        }

        internal static bool AreModifierStatesReleased(short shift, short control, short alt, short leftWin, short rightWin) =>
            shift >= 0 && control >= 0 && alt >= 0 && leftWin >= 0 && rightWin >= 0;

        private static bool ModifiersAreReleased() => AreModifierStatesReleased(
            GetAsyncKeyState(0x10), GetAsyncKeyState(0x11), GetAsyncKeyState(0x12),
            GetAsyncKeyState(0x5B), GetAsyncKeyState(0x5C));

        public static bool ReplaceSelection(string text)
        {
            if (text.Any(character => char.IsControl(character) || character is '\ufffc' or '\u2028' or '\u2029'))
            {
                return false;
            }

            if (text.Length == 0)
            {
                var deletion = new[] { Key(VirtualKeyBack, 0), Key(VirtualKeyBack, KeyEventKeyUp) };
                if (!ModifiersAreReleased()) return false;
                return SendInput((uint)deletion.Length, deletion, StructSize) == deletion.Length;
            }

            var inputs = new INPUT[text.Length * 2];
            for (var index = 0; index < text.Length; index++)
            {
                inputs[index * 2] = UnicodeKey(text[index], 0);
                inputs[index * 2 + 1] = UnicodeKey(text[index], KeyEventKeyUp);
            }

            return ModifiersAreReleased() && SendInput((uint)inputs.Length, inputs, StructSize) == inputs.Length;
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

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

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
