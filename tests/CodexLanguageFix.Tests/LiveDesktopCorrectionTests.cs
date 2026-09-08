using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;
using CodexLanguageFix.Windows;
using Xunit.Abstractions;

namespace CodexLanguageFix.Tests;

[CollectionDefinition("Live desktop correction", DisableParallelization = true)]
public sealed class LiveDesktopCorrectionCollection { }

[Collection("Live desktop correction")]
public sealed class LiveDesktopCorrectionTests(ITestOutputHelper output)
{
    private const string OriginalFixture = "Das ist ein fehler.";
    private const string CorrectedFixture = "Das ist ein Fehler.";
    private const string OriginalParagraphFixture = "Das ist ein fehler.\n\n- This is an test.\n- Grüße 🙂 an test@example.com";
    private const string CorrectedParagraphFixture = "Das ist ein Fehler.\n\n- This is a test.\n- Grüße 🙂 an test@example.com";
    private static readonly PreparedFixture[] SentenceFixtures = [
        new(OriginalFixture, CorrectedFixture),
        new(OriginalFixture + "\r", CorrectedFixture + "\r"),
        new(OriginalFixture + "\n", CorrectedFixture + "\n"),
        new(OriginalFixture + "\r\n", CorrectedFixture + "\r\n")];
    // Only these complete, synthetic variants are authorized; observed editor text is never normalized.
    private static readonly PreparedFixture[] ParagraphFixtures =
        (from newline in new[] { "\n", "\r\n" }
         from suffix in new[] { "", "\r", "\n", "\r\n" }
         select new PreparedFixture(
             OriginalParagraphFixture.Replace("\n", newline, StringComparison.Ordinal) + suffix,
             CorrectedParagraphFixture.Replace("\n", newline, StringComparison.Ordinal) + suffix))
        .Append(new PreparedFixture(
            "Das ist ein fehler.\n• This is an test.\n• Grüße 🙂 an test@example.com",
            "Das ist ein Fehler.\n• This is a test.\n• Grüße 🙂 an test@example.com"))
        .ToArray();

    [Fact]
    public void PreparedSyntheticSentence_IsCorrectedAndUndoneThroughOwnOverlay()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_LIVE_TEST") != "1")
        {
            return;
        }

        RunOverlayRoundTrip(SentenceFixtures, ComposerHost.Codex);
    }

    [Fact]
    public void PreparedSyntheticEndInsertion_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_END_INSERTION_TEST") != "1") return;
        RunNativeRoundTrip([
            new("Das ist ein Test", "Das ist ein Test."),
            new("Das ist ein Test\n", "Das ist ein Test.\n"),
            new("Das ist ein Test\r\n", "Das ist ein Test.\r\n")], ComposerHost.Codex);
    }

    [Fact]
    public void PreparedSyntheticEndReplacement_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_END_REPLACEMENT_TEST") != "1") return;
        RunNativeRoundTrip([
            new("Das ist ein Test", "Das ist ein Tesx"),
            new("Das ist ein Test\n", "Das ist ein Tesx\n"),
            new("Das ist ein Test\r\n", "Das ist ein Tesx\r\n")], ComposerHost.Codex);
    }

    [Fact]
    public void PreparedSyntheticBoundaryMatrix_DirectNativeRoundTrips()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_BOUNDARY_MATRIX_TEST") != "1") return;
        foreach (var corrected in new[] {
            "das ist ein Test", "as ist ein Test", "🙂 Das ist ein Test",
            "Das ist wirklich ein Test", "Das ist Test", "Das ist ein Tést",
            "Das ist ein Test 🙂", "Das ist ein Tes", "Dies ist eine Prüfung" })
        {
            RunNativeRoundTrip((from suffix in new[] { "", "\n", "\r\n", "\n\n", "\r\n\r\n" }
                select new PreparedFixture("Das ist ein Test" + suffix, corrected + suffix)).ToArray(),
                ComposerHost.Codex);
        }
    }

    // Opt in with CODEX_LANGUAGE_FIX_DESKTOP_PARAGRAPH_LIVE_TEST=1 after preparing the exact paragraph/list fixture.
    [Fact]
    public void PreparedSyntheticParagraphList_IsCorrectedAndUndoneThroughOwnOverlay()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_PARAGRAPH_LIVE_TEST") != "1") return;
        RunOverlayRoundTrip(ParagraphFixtures, ComposerHost.Codex);
    }

    // Opt in with ANTIGRAVITY_DESKTOP_LIVE_TEST=1 after preparing the exact sentence fixture in Antigravity.
    [Fact]
    public void PreparedSyntheticAntigravitySentence_IsCorrectedAndUndoneThroughOwnOverlay()
    {
        if (Environment.GetEnvironmentVariable("ANTIGRAVITY_DESKTOP_LIVE_TEST") != "1") return;
        RunOverlayRoundTrip(SentenceFixtures, ComposerHost.Antigravity);
    }

    // Read-only discovery: CODEX_LANGUAGE_FIX_DESKTOP_FIXTURE_PROBE=1. Never authorizes a write or new fixture variant.
    [Fact]
    public void PreparedSyntheticParagraphList_ReadOnlyReportsVerifiedFixtureEncoding()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_FIXTURE_PROBE") != "1") return;

        var snapshot = new CodexComposerAccessor().TryCaptureFocusedComposer();
        Assert.True(snapshot is not null && snapshot.Host == ComposerHost.Codex,
            "Die vorbereitete Codex-Testeingabe ist nicht eindeutig erreichbar.");
        var text = snapshot!.Text;
        var cursor = 0;
        foreach (var block in new[] { OriginalFixture, "This is an test.", "Grüße 🙂 an test@example.com" })
        {
            var position = text.IndexOf(block, cursor, StringComparison.Ordinal);
            Assert.True(position >= cursor && IsFixtureDecoration(text.AsSpan(cursor, position - cursor)),
                "Die Zeichenfolge enthält nicht ausschließlich die bekannten synthetischen Bausteine und Listentrenner; keine Textausgabe.");
            cursor = position + block.Length;
        }
        Assert.True(IsFixtureDecoration(text.AsSpan(cursor)),
            "Die Zeichenfolge enthält einen fremden Suffix; keine Textausgabe.");

        output.WriteLine("synthetic_fixture_json=" + JsonSerializer.Serialize(text));

        static bool IsFixtureDecoration(ReadOnlySpan<char> decoration)
        {
            foreach (var character in decoration)
            {
                if (!char.IsWhiteSpace(character) && character is not ('-' or '+' or '*' or '•' or '◦' or '▪' or '‣' or '⁃'))
                    return false;
            }
            return true;
        }
    }

    private void RunOverlayRoundTrip(IReadOnlyList<PreparedFixture> fixtures, ComposerHost expectedHost)
    {
        var accessor = new CodexComposerAccessor();
        var (original, correctedText) = CapturePreparedFixture(accessor, fixtures, expectedHost);
        var originalText = original.Text;
        output.WriteLine($"fixtureCharacters={originalText.Length}; lineBreaks={originalText.Count(c => c == '\n')}");
        var correctionStates = PlannedStates(originalText, correctedText);
        var undoStates = PlannedStates(correctedText, originalText);

        var processes = Process.GetProcessesByName("CodexLanguageFix");
        try
        {
            Assert.True(processes.Length == 1,
                "Für den Live-Test muss genau ein CodexLanguageFix-Appprozess laufen.");
            var app = processes[0];
            var localizer = new AppLocalizer(new SettingsService().Load().Language);
            var correct = WaitForButton(app, localizer.Get(AppText.CorrectAutomationName));
            AssertCurrentText(originalText);
            var correctWindow = OwnButtonWindow(app, correct);
            ReportActivationState(app, correctWindow, original.HostWindow, "correct_before_click");
            ClickOwnButton(app, correct, original.HostWindow, () => AssertCurrentText(originalText));
            ReportActivationState(app, correctWindow, original.HostWindow, "correct_after_click");
            WaitForText(correctedText, correctionStates);

            var undo = WaitForButton(app, localizer.Get(AppText.Undo));
            AssertCurrentText(correctedText);
            var undoWindow = OwnButtonWindow(app, undo);
            ReportActivationState(app, undoWindow, original.HostWindow, "undo_before_click");
            ClickOwnButton(app, undo, original.HostWindow, () => AssertCurrentText(correctedText));
            ReportActivationState(app, undoWindow, original.HostWindow, "undo_after_click");
            WaitForText(originalText, undoStates);
            AssertCurrentText(originalText);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        void AssertCurrentText(string expected)
        {
            var current = accessor.TryCaptureFocusedComposer();
            Assert.True(original.SameEditor(current) && current!.Text == expected,
                "Editoridentität oder synthetische Testeingabe hat sich vor der Aktion geändert.");
        }

        void WaitForText(string expected, IReadOnlySet<string> permittedStates)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(60))
            {
                var current = accessor.TryCaptureFocusedComposer();
                Assert.True(original.SameEditor(current),
                    "Der ursprüngliche Testeditor ist während der Prüfung nicht mehr aktiv.");
                if (current!.Text == expected) return;
                Assert.True(permittedStates.Contains(current.Text),
                    "Die Testeingabe wurde unerwartet verändert; es erfolgen keine weiteren UI-Aktionen.");
                Thread.Sleep(100);
            }

            Assert.Fail("Die erwartete vollständig gelesene Testeingabe wurde innerhalb von 60 Sekunden nicht erreicht.");
        }
    }

    [Fact]
    public void PreparedSyntheticSentence_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_NATIVE_TEST") != "1")
        {
            return;
        }

        RunNativeRoundTrip(SentenceFixtures, ComposerHost.Codex);
    }

    // Opt in with CODEX_LANGUAGE_FIX_DESKTOP_PARAGRAPH_NATIVE_TEST=1; no provider or overlay invocation.
    [Fact]
    public void PreparedSyntheticParagraphList_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_PARAGRAPH_NATIVE_TEST") != "1") return;
        RunNativeRoundTrip(ParagraphFixtures, ComposerHost.Codex);
    }

    // Opt in with ANTIGRAVITY_DESKTOP_NATIVE_TEST=1; only the exact Antigravity sentence fixture is authorized.
    [Fact]
    public void PreparedSyntheticAntigravitySentence_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("ANTIGRAVITY_DESKTOP_NATIVE_TEST") != "1") return;
        RunNativeRoundTrip(SentenceFixtures, ComposerHost.Antigravity);
    }

    private void RunNativeRoundTrip(IReadOnlyList<PreparedFixture> fixtures, ComposerHost expectedHost)
    {
        var accessor = new CodexComposerAccessor();
        var (original, correctedText) = CapturePreparedFixture(accessor, fixtures, expectedHost);
        var originalText = original.Text;
        output.WriteLine($"fixtureCharacters={originalText.Length}; lineBreaks={originalText.Count(c => c == '\n')}");

        var applied = accessor.TryReplace(original, originalText, correctedText);
        if (!applied && original.NativeElement is AutomationElement native
            && original.SameEditor(accessor.TryCaptureFocusedComposer())
            && accessor.TryRefresh(original)?.Text == originalText)
        {
            var pattern = (TextPattern)native.GetCurrentPattern(TextPattern.Pattern);
            foreach (var selected in pattern.GetSelection())
            {
                var prefix = pattern.DocumentRange.Clone();
                prefix.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End,
                    selected, System.Windows.Automation.Text.TextPatternRangeEndpoint.Start);
                var through = pattern.DocumentRange.Clone();
                through.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End,
                    selected, System.Windows.Automation.Text.TextPatternRangeEndpoint.End);
                var suffix = pattern.DocumentRange.Clone();
                suffix.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start,
                    selected, System.Windows.Automation.Text.TextPatternRangeEndpoint.End);
                output.WriteLine(JsonSerializer.Serialize(new {
                    selectionLength = selected.GetText(-1).Length,
                    prefixLength = prefix.GetText(-1).Length,
                    endPrefixLength = through.GetText(-1).Length,
                    suffixLength = suffix.GetText(-1).Length,
                    prefixIsWholeOriginal = prefix.GetText(-1) == originalText,
                    endPrefixIsWholeOriginal = through.GetText(-1) == originalText
                }));
            }
        }
        Assert.True(applied, accessor.LastWriteStatus);
        var corrected = accessor.TryRefresh(original);
        Assert.True(original.SameEditor(corrected) && corrected!.Text == correctedText,
            "Die native Korrektur wurde nicht exakt im ursprünglichen Testeditor bestätigt.");
        var active = accessor.TryCaptureFocusedComposer();
        Assert.True(original.SameEditor(active) && active!.Text == correctedText,
            "Vor dem nativen Rückweg ist nicht mehr derselbe unveränderte Testeditor aktiv.");

        Assert.True(accessor.TryReplace(corrected!, correctedText, originalText), accessor.LastWriteStatus);
        var restored = accessor.TryRefresh(original);
        Assert.True(original.SameEditor(restored) && restored!.Text == originalText,
            "Der native Rückweg hat die synthetische Testeingabe nicht exakt wiederhergestellt.");
    }

    private static (ComposerSnapshot Original, string CorrectedText) CapturePreparedFixture(
        CodexComposerAccessor accessor, IReadOnlyList<PreparedFixture> fixtures, ComposerHost expectedHost)
    {
        var original = accessor.TryCaptureFocusedComposer();
        Assert.True(original is not null && original.Host == expectedHost,
            "Die vorbereitete Testeingabe ist nicht eindeutig im explizit erwarteten Host erreichbar.");
        var fixture = fixtures.FirstOrDefault(candidate => candidate.Original == original!.Text);
        Assert.True(fixture is not null,
            "Der Live-Test darf ausschließlich eine vollständig übereinstimmende synthetische Fixturevariante bedienen.");
        return (original!, fixture!.Corrected);
    }

    private static IReadOnlySet<string> PlannedStates(string original, string corrected)
    {
        var plan = ComposerEditPlan.Create(original, corrected);
        Assert.True(plan is not null, "Für die synthetische Fixture muss ein strukturerhaltender Schreibplan vorliegen.");
        var states = new HashSet<string>(StringComparer.Ordinal) { original };
        var current = original;
        foreach (var edit in plan!.Edits)
        {
            current = current.Remove(edit.Start, edit.Original.Length).Insert(edit.Start, edit.Replacement);
            states.Add(current);
        }
        return states;
    }

    private sealed record PreparedFixture(string Original, string Corrected);

    private static AutomationElement WaitForButton(Process app, string name)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            Assert.True(!app.HasExited, "Der zu Beginn ermittelte Appprozess wurde beendet.");
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id));
            var buttonCondition = new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, name),
                    new PropertyCondition(AutomationElement.IsEnabledProperty, true),
                    new PropertyCondition(AutomationElement.IsOffscreenProperty, false));
            var buttons = new List<AutomationElement>();
            foreach (AutomationElement window in windows)
            {
                foreach (AutomationElement button in window.FindAll(TreeScope.Descendants, buttonCondition))
                {
                    buttons.Add(button);
                }
            }
            Assert.True(buttons.Count <= 1, "Die angeforderte Schaltfläche im eigenen Appprozess ist nicht eindeutig.");
            if (buttons.Count == 1) return buttons[0];
            Thread.Sleep(100);
        }

        throw new InvalidOperationException("Die sichtbare aktive Overlay-Schaltfläche wurde nicht im eigenen Appprozess gefunden.");
    }

    // Nur die opt-in Fixturetests dürfen die eigene, zuvor eindeutig ermittelte Overlay-Schaltfläche anklicken.
    private static void ClickOwnButton(Process app, AutomationElement button, nint originalHost, Action verifyFixture)
    {
        Assert.True(!app.HasExited && button.Current.ProcessId == app.Id
            && button.Current.IsEnabled && !button.Current.IsOffscreen,
            "Die zuvor verifizierte Overlay-Schaltfläche ist nicht mehr verfügbar.");
        var clickable = button.GetClickablePoint();
        Assert.True(double.IsFinite(clickable.X) && double.IsFinite(clickable.Y),
            "Die eigene Overlay-Schaltfläche besitzt keinen gültigen beobachteten Klickpunkt.");
        var point = new NativePoint { X = checked((int)Math.Round(clickable.X)), Y = checked((int)Math.Round(clickable.Y)) };
        var expectedRoot = GetAncestor(OwnButtonWindow(app, button), 2); // GA_ROOT
        VerifyClickTarget();
        Assert.True(SetPhysicalCursorPos(point.X, point.Y), "Der beobachtete physische Klickpunkt konnte nicht erreicht werden.");
        verifyFixture();
        Assert.True(GetPhysicalCursorPos(out var actual) && actual.X == point.X && actual.Y == point.Y,
            "Die tatsächliche Cursorposition entspricht nicht dem verifizierten Klickpunkt; kein Klick.");
        VerifyClickTarget();

        MouseInputEvent[] click = [
            new() { Mouse = new NativeMouseInput { Flags = 0x0002 } }, // MOUSEEVENTF_LEFTDOWN
            new() { Mouse = new NativeMouseInput { Flags = 0x0004 } }]; // MOUSEEVENTF_LEFTUP
        Assert.Equal(CodexComposerAccessor.NativeInputSize, Marshal.SizeOf<MouseInputEvent>());
        Assert.Equal(2u, SendInput(2, click, Marshal.SizeOf<MouseInputEvent>()));

        void VerifyClickTarget()
        {
            Assert.True(originalHost != 0 && GetForegroundWindow() == originalHost,
                "Das ursprüngliche Hostfenster ist nicht mehr tatsächlich im Vordergrund; kein Klick.");
            Assert.True(!app.HasExited && button.Current.ProcessId == app.Id && button.Current.IsEnabled
                && !button.Current.IsOffscreen && button.Current.BoundingRectangle.Contains(new System.Windows.Point(point.X, point.Y)),
                "Die verifizierte Schaltfläche hat sich vor dem Klick verändert.");
            var hit = WindowFromPhysicalPoint(point);
            var root = GetAncestor(hit, 2);
            _ = GetWindowThreadProcessId(hit, out var hitProcessId);
            _ = GetWindowThreadProcessId(root, out var rootProcessId);
            Assert.True(hit != 0 && expectedRoot != 0 && root == expectedRoot
                && hitProcessId == app.Id && rootProcessId == app.Id,
                "Am beobachteten Klickpunkt liegt nicht exakt das eigene verifizierte Overlay-Fenster; kein Klick.");
        }
    }

    private static nint OwnButtonWindow(Process app, AutomationElement button)
    {
        var current = button;
        nint handle = 0;
        for (var depth = 0; depth < 20 && current is not null && current.Current.ProcessId == app.Id; depth++)
        {
            if (current.Current.NativeWindowHandle != 0)
            {
                handle = current.Current.NativeWindowHandle;
                break;
            }
            current = TreeWalker.ControlViewWalker.GetParent(current);
        }
        _ = GetWindowThreadProcessId(handle, out var windowProcessId);
        Assert.True(handle != 0 && windowProcessId == app.Id,
            "Die Diagnose darf ausschließlich das native Fenster der eigenen Overlay-Schaltfläche lesen.");
        return handle;
    }

    private void ReportActivationState(Process app, nint handle, nint originalHost, string phase)
    {
        _ = GetWindowThreadProcessId(handle, out var windowProcessId);
        var windowAvailable = windowProcessId == app.Id;
        var styles = windowAvailable ? GetWindowLongPtr(handle, -20).ToInt64() : 0;
        var foreground = GetForegroundWindow();
        _ = GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        output.WriteLine($"{phase}: window_available={windowAvailable}; no_activate={(styles & 0x08000000L) != 0}; tool_window={(styles & 0x00000080L) != 0}; foreground_process_own={foregroundProcessId == app.Id}; original_host_foreground={foreground == originalHost}");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPhysicalPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPhysicalCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, MouseInputEvent[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputEvent
    {
        public uint Type;
        public NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
