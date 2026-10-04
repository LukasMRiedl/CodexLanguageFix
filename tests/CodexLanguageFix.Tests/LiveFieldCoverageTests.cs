using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Windows;
using Xunit.Abstractions;

namespace CodexLanguageFix.Tests;

[Collection("Live desktop correction")]
public sealed class LiveFieldCoverageTests(ITestOutputHelper output)
{
    [Fact]
    public void FocusedField_ReadOnlyEligibilityAudit()
    {
        var hostSetting = Environment.GetEnvironmentVariable("LANGUAGE_FIX_FIELD_AUDIT_HOST");
        if (hostSetting is null) return;
        using var dpiScope = new LiveDpiScope();
        Assert.True(hostSetting is "Codex" or "Hermes");
        var expectedStatus = Environment.GetEnvironmentVariable("LANGUAGE_FIX_FIELD_AUDIT_STATUS");
        Assert.True(expectedStatus is "captured" or "focused_non_composer" or "ambiguous_placeholder");
        var accessor = new CodexComposerAccessor();
        var snapshot = accessor.TryCaptureFocusedComposer();
        var element = snapshot?.NativeElement as AutomationElement ?? AutomationElement.FocusedElement;
        var valueLength = element?.TryGetCurrentPattern(ValuePattern.Pattern, out var value) == true
            ? ((ValuePattern)value!).Current.Value.Length : (int?)null;
        object? text = null;
        var textRange = element?.TryGetCurrentPattern(TextPattern.Pattern, out text) == true
            ? ((TextPattern)text!).DocumentRange : null;
        output.WriteLine(JsonSerializer.Serialize(new { accessor.LastCaptureStatus, accessor.LastCaptureHost,
            accessor.LastFieldCategory, characterCount = snapshot?.Text.Length, snapshot?.Bounds,
            nameLength = element?.Current.Name.Length,
            nameEqualsText = snapshot is not null && string.Equals(element?.Current.Name,
                snapshot.Text.TrimEnd('\r', '\n'), StringComparison.Ordinal),
            snapshot?.PlacementBounds, snapshot?.ReadMethod, type = element?.Current.ControlType.ProgrammaticName,
            focusedType = AutomationElement.FocusedElement?.Current.ControlType.ProgrammaticName,
            focusedProcessMatches = AutomationElement.FocusedElement?.Current.ProcessId == element?.Current.ProcessId,
            hasKeyboardFocus = element?.Current.HasKeyboardFocus,
            className = element?.Current.ClassName, automationId = element?.Current.AutomationId,
            valueLength, helpTextLength = element?.Current.HelpText.Length,
            textHidden = textRange?.GetAttributeValue(TextPattern.IsHiddenAttribute)?.ToString(),
            textReadOnly = textRange?.GetAttributeValue(TextPattern.IsReadOnlyAttribute)?.ToString(),
            descendants = element?.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
                .Select(child => new { type = child.Current.ControlType.ProgrammaticName,
                    nameLength = child.Current.Name.Length, className = child.Current.ClassName,
                    readOnly = text is TextPattern parentText
                        ? parentText.RangeFromChild(child).GetAttributeValue(TextPattern.IsReadOnlyAttribute)?.ToString() : null }).ToArray(),
            children = textRange?.GetChildren().Select(child => new {
                type = child.Current.ControlType.ProgrammaticName, nameLength = child.Current.Name.Length,
                className = child.Current.ClassName, child.Current.IsOffscreen }).ToArray() }));
        Assert.True(accessor.LastCaptureStatus == expectedStatus, "Die geprüfte Feldzuordnung weicht vom erwarteten Status ab.");
        if (expectedStatus != "captured") Assert.Null(snapshot);
        Assert.True(accessor.LastCaptureHost == (hostSetting == "Codex" ? ComposerHost.Codex : ComposerHost.Hermes));
    }

    [Fact]
    public void PreparedSyntheticField_HasVerifiedPlacementAndFreshGeometry()
    {
        var hostSetting = Environment.GetEnvironmentVariable("LANGUAGE_FIX_FIELD_PROBE_HOST");
        if (hostSetting is null) return;
        using var dpiScope = new LiveDpiScope();
        Assert.True(hostSetting is "Codex" or "Hermes", "Die Feldprüfung verlangt einen expliziten unterstützten Host.");
        var expectedHost = hostSetting == "Hermes" ? ComposerHost.Hermes : ComposerHost.Codex;
        var accessor = new CodexComposerAccessor();
        var stopwatch = Stopwatch.StartNew();
        var first = accessor.TryCaptureFocusedComposer();
        stopwatch.Stop();
        output.WriteLine(JsonSerializer.Serialize(new { accessor.LastCaptureStatus, accessor.LastCaptureHost,
            accessor.LastFieldCategory, captureMilliseconds = stopwatch.Elapsed.TotalMilliseconds }));
        Assert.True(first is not null && first.Host == expectedHost, "Das vorbereitete Feld wurde nicht eindeutig erkannt.");
        var fixture = Environment.GetEnvironmentVariable("LANGUAGE_FIX_FIELD_PROBE_FIXTURE") == "paragraph"
            ? "Das ist ein fehler.\n\n- This is an test.\n- Grüße 🙂 an test@example.com"
            : "Das ist ein fehler.";
        Assert.True(first!.Text == fixture || first.Text == fixture + "\r" || first.Text == fixture + "\n"
            || first.Text == fixture + "\r\n", "Nur die vollständige synthetische Fixture darf geprüft werden.");
        Assert.True(first.PlacementBounds is not null, "Es fehlen verifizierte Fenstergrenzen.");
        var placementBounds = first.PlacementBounds!.Value;
        var scale = GetDpiForWindow(first.HostWindow) / 96d;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Assert.True(GetMonitorInfo(MonitorFromWindow(first.HostWindow, 2), ref monitor));
        var screen = new System.Windows.Rect(monitor.Work.Left, monitor.Work.Top,
            monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
        var placement = OverlayPlacement.Find(first.Bounds, first.EditorBounds!.Value,
            first.OccupiedBounds ?? [], screen, scale, first.RightControlBounds, placementBounds);
        output.WriteLine(JsonSerializer.Serialize(new { first.Host, first.FieldCategory, first.ReadMethod,
            editor = first.EditorBounds, surface = first.Bounds, placementBounds, placement,
            obstacles = first.OccupiedBounds?.Count, scale, screen }));
        Assert.True(placement is not null, "Es wurde keine freie Aa-Position für das vorbereitete Feld gefunden.");
        Assert.True(placementBounds.Contains(placement!.Value));
        Assert.True(screen.Contains(placement.Value));
        Assert.False(first.EditorBounds.Value.IntersectsWith(placement.Value));
        Assert.DoesNotContain(first.OccupiedBounds ?? [], obstacle => obstacle.IntersectsWith(placement.Value));
        var layoutCount = accessor.LayoutCaptureCount;
        Thread.Sleep(760);
        stopwatch.Restart();
        var second = accessor.TryPollFocusedComposer();
        stopwatch.Stop();
        output.WriteLine(JsonSerializer.Serialize(new { pollMilliseconds = stopwatch.Elapsed.TotalMilliseconds }));
        Assert.True(first.SameEditor(second) && second!.Text == first.Text);
        Assert.True(accessor.LayoutCaptureCount > layoutCount, "Die periodische Prüfung muss die Hindernisse neu erfassen.");
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
}
