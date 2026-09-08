using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using CodexLanguageFix.Windows;
using Xunit.Abstractions;

namespace CodexLanguageFix.Tests;

[Collection("Live desktop correction")]
public sealed class LiveDesktopGeometryProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void CapturedComposerAndNearbyAncestors_ReportGeometryWithoutText()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_GEOMETRY_PROBE") != "1") return;

        var accessor = new CodexComposerAccessor();
        var snapshot = accessor.TryCaptureFocusedComposer();
        output.WriteLine(JsonSerializer.Serialize(new { captureStatus = accessor.LastCaptureStatus }));
        Assert.True(snapshot is not null, "Kein eindeutig erreichbarer Composer für die Geometrieprüfung.");
        output.WriteLine(JsonSerializer.Serialize(new
        {
            snapshot!.Host,
            characterCount = snapshot.Text.Length,
            isEmpty = string.IsNullOrWhiteSpace(snapshot.Text),
            hostWindow = snapshot.HostWindow.ToInt64(),
            surface = Bounds(snapshot.Bounds),
            editor = Bounds(snapshot.EditorBounds ?? snapshot.Bounds),
            occupied = snapshot.OccupiedBounds?.Select(Bounds).ToArray(),
            snapshot.ReadMethod
        }));

        var element = snapshot.NativeElement as AutomationElement;
        Assert.True(element is not null, "Der Composer stellt kein natives UIA-Element bereit.");
        WriteElement("editor", element!);
        var hasValuePattern = element!.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject);
        var value = hasValuePattern ? (ValuePattern)valueObject : null;
        var hasTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out var textObject);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            hasValuePattern,
            valueIsReadOnly = value?.Current.IsReadOnly,
            valueLength = value?.Current.Value.Length,
            isValueEmpty = value is null ? (bool?)null : value.Current.Value.Length == 0,
            hasTextPattern,
            textLength = hasTextPattern ? ((TextPattern)textObject).DocumentRange.GetText(-1).Length : (int?)null
        }));
        if (AutomationElement.FocusedElement is { } focused) WriteElement("focused", focused);
        var ariaProperty = element.GetSupportedProperties().FirstOrDefault(property =>
            property is not null && property.ProgrammaticName.Contains("AriaProperties", StringComparison.Ordinal));
        var aria = ariaProperty is null ? null : element.GetCurrentPropertyValue(ariaProperty) as string;
        var helpText = element.Current.HelpText;
        var textPattern = hasTextPattern ? (TextPattern)textObject : null;
        var documentRange = textPattern?.DocumentRange;
        output.WriteLine(JsonSerializer.Serialize(new
        {
            role = "editor-empty-state",
            ariaPropertiesAvailable = ariaProperty is not null,
            ariaPropertiesLength = aria?.Length,
            helpTextLength = helpText.Length,
            helpTextEqualsValue = value is not null && helpText.Length > 0 && helpText == value.Current.Value,
            helpTextEqualsText = documentRange is not null && helpText.Length > 0 && helpText == documentRange.GetText(-1),
            textIsReadOnly = Attribute(documentRange, TextPattern.IsReadOnlyAttribute),
            textIsHidden = Attribute(documentRange, TextPattern.IsHiddenAttribute),
            selectionLengths = textPattern?.GetSelection().Select(range => range.GetText(-1).Length).ToArray()
        }));
        var children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        output.WriteLine(JsonSerializer.Serialize(new { role = "editor-children", count = children.Count }));
        for (var index = 0; index < Math.Min(20, children.Count); index++)
        {
            WriteElement($"editor-child-{index}", children[index]);
            if (textPattern is not null) WriteChildRange(index, textPattern, children[index]);
        }
        var editorBounds = snapshot.EditorBounds ?? snapshot.Bounds;
        var current = element;
        var nearbyReported = false;
        var textReported = false;
        for (var depth = 1; depth <= 6; depth++)
        {
            current = TreeWalker.ControlViewWalker.GetParent(current!);
            if (current is null) break;
            WriteElement($"ancestor-{depth}", current);
            var rectangle = current.Current.BoundingRectangle;
            if (!textReported && current.Current.ControlType == ControlType.Document
                && current.Current.AutomationId == "RootWebArea")
            {
                textReported = true;
                var vicinity = editorBounds;
                vicinity.Inflate(200, 200);
                var textElements = current.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                var count = 0;
                foreach (AutomationElement textElement in textElements)
                {
                    if (textElement.Current.IsOffscreen || textElement.Current.BoundingRectangle.IsEmpty
                        || !vicinity.IntersectsWith(textElement.Current.BoundingRectangle)) continue;
                    if (count++ == 50) break;
                    var visible = CodexComposerAccessor.TryGetVisibleTextBounds(textElement, current);
                    output.WriteLine(JsonSerializer.Serialize(new
                    {
                        role = "nearby-text-geometry",
                        aggregate = Bounds(textElement.Current.BoundingRectangle),
                        hasVerifiedVisibleRanges = visible is not null,
                        visibleRectangles = visible?.Select(Bounds).ToArray()
                    }));
                    WriteAncestors($"nearby-text-{count}", textElement);
                }

                var overlapCandidates = current.FindAll(TreeScope.Descendants, new OrCondition(
                    new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
                var overlapCount = 0;
                foreach (AutomationElement control in overlapCandidates)
                {
                    if (control.Current.IsOffscreen || control.Current.BoundingRectangle.IsEmpty
                        || !snapshot.Bounds.IntersectsWith(control.Current.BoundingRectangle)
                        || control.GetRuntimeId().SequenceEqual(snapshot.RuntimeId)
                        || CodexComposerAccessor.IsSupportedComposerShape(control.Current.ControlType,
                            control.Current.ClassName, control.Current.AutomationId)) continue;
                    if (overlapCount == 50) break;
                    var role = $"overlapping-control-{overlapCount++}";
                    WriteElement(role, control);
                    WriteAncestors(role, control);
                }
            }
            if (!nearbyReported && rectangle.Contains(editorBounds)
                && (current.Current.ControlType == ControlType.Group || current.Current.ControlType == ControlType.Window))
            {
                nearbyReported = true;
                var neighborhood = editorBounds;
                neighborhood.Inflate(200, 200);
                var nearbyCandidates = current.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                var nearbyCount = 0;
                foreach (AutomationElement button in nearbyCandidates)
                {
                    if (button.Current.IsOffscreen || button.Current.BoundingRectangle.IsEmpty
                        || !neighborhood.IntersectsWith(button.Current.BoundingRectangle)) continue;
                    if (nearbyCount == 50) break;
                    WriteElement($"nearby-button-{nearbyCount++}", button);
                }
                output.WriteLine(JsonSerializer.Serialize(new { nearbyAncestorDepth = depth, nearbyButtonsReported = nearbyCount }));
            }

            var small = !rectangle.IsEmpty && rectangle.Contains(editorBounds)
                && rectangle.Width <= editorBounds.Width + 400
                && rectangle.Height <= editorBounds.Height + 400;
            if (!small)
            {
                output.WriteLine(JsonSerializer.Serialize(new { depth, descendants = "skipped-large-ancestor" }));
                continue;
            }

            var controls = current.FindAll(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
            output.WriteLine(JsonSerializer.Serialize(new { depth, controlCount = controls.Count, reported = Math.Min(50, controls.Count) }));
            for (var index = 0; index < Math.Min(50, controls.Count); index++)
                WriteElement($"ancestor-{depth}-control-{index}", controls[index]);
        }
    }

    private void WriteElement(string role, AutomationElement element)
    {
        var current = element.Current;
        var hasScrollPattern = element.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrollObject);
        var scroll = hasScrollPattern ? (ScrollPattern)scrollObject : null;
        output.WriteLine(JsonSerializer.Serialize(new
        {
            role,
            type = current.ControlType.ProgrammaticName,
            className = Limit(current.ClassName),
            automationId = Limit(current.AutomationId),
            bounds = Bounds(current.BoundingRectangle),
            current.IsOffscreen,
            current.IsKeyboardFocusable,
            hasScrollPattern,
            horizontallyScrollable = scroll?.Current.HorizontallyScrollable,
            verticallyScrollable = scroll?.Current.VerticallyScrollable,
            horizontalViewSize = scroll?.Current.HorizontalViewSize,
            verticalViewSize = scroll?.Current.VerticalViewSize
        }));
    }

    private void WriteAncestors(string role, AutomationElement element)
    {
        var current = element;
        for (var depth = 1; depth <= 5; depth++)
        {
            current = TreeWalker.ControlViewWalker.GetParent(current);
            if (current is null) break;
            WriteElement($"{role}-ancestor-{depth}", current);
        }
    }

    private void WriteChildRange(int index, TextPattern pattern, AutomationElement child)
    {
        try
        {
            var range = pattern.RangeFromChild(child);
            var prefix = pattern.DocumentRange.Clone();
            prefix.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End, range,
                System.Windows.Automation.Text.TextPatternRangeEndpoint.Start);
            var prefixLength = prefix.GetText(-1).Length;
            prefix.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End, range,
                System.Windows.Automation.Text.TextPatternRangeEndpoint.End);
            var attributes = new Dictionary<int, object>();
            AutomationTextAttribute[] requested = [TextPattern.IsReadOnlyAttribute, TextPattern.IsHiddenAttribute,
                TextPattern.IsItalicAttribute, TextPattern.IsSubscriptAttribute, TextPattern.IsSuperscriptAttribute,
                TextPattern.CultureAttribute, TextPattern.FontWeightAttribute];
            foreach (var attribute in requested)
            {
                var value = range.GetAttributeValue(attribute);
                if (value is bool or int) attributes.Add(attribute.Id, value);
            }

            output.WriteLine(JsonSerializer.Serialize(new
            {
                role = $"editor-child-{index}-range",
                textLength = range.GetText(-1).Length,
                prefixLength,
                endPrefixLength = prefix.GetText(-1).Length,
                attributes
            }));
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException
            or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            output.WriteLine(JsonSerializer.Serialize(new { role = $"editor-child-{index}-range", unavailable = exception.GetType().Name }));
        }
    }

    private static string Bounds(Rect bounds) => bounds.ToString(CultureInfo.InvariantCulture);
    private static string Attribute(System.Windows.Automation.Text.TextPatternRange? range, AutomationTextAttribute attribute)
    {
        if (range is null) return "unavailable";
        var value = range.GetAttributeValue(attribute);
        return value is bool boolean ? boolean ? "true" : "false"
            : ReferenceEquals(value, TextPattern.MixedAttributeValue) ? "mixed" : "unsupported";
    }
    private static string Limit(string value) => value.Length <= 512 ? value : value[..512];
}
