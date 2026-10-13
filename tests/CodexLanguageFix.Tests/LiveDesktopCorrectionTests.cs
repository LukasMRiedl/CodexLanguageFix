using System.Diagnostics;
using System.Globalization;
using System.Reflection;
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
    public void PreparedSyntheticCodexParagraph_MonitorTransitionRoundTrips()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_MONITOR_TRANSITION_TEST") != "1") return;
        using var dpiScope = new LiveDpiScope();
        var accessor = new CodexComposerAccessor();
        var (original, _) = CapturePreparedFixture(accessor, ParagraphFixtures, ComposerHost.Codex);
        Assert.True(GetWindowRect(original.HostWindow, out var previous));
        // Optional, explicitly observed pre-test geometry for restoring this local test session.
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_RESTORE_WINDOW_RECT") is { } restoreRect)
        {
            var parts = restoreRect.Split(',').Select(int.Parse).ToArray();
            Assert.Equal(4, parts.Length);
            Assert.True(parts[2] > parts[0] && parts[3] > parts[1]);
            previous = new NativeRect { Left = parts[0], Top = parts[1], Right = parts[2], Bottom = parts[3] };
        }
        var primary = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        Assert.True(GetMonitorInfo(MonitorFromPoint(new NativePoint(), 2), ref primary));
        try
        {
            Assert.True(SetWindowPos(original.HostWindow, nint.Zero,
                primary.Work.Left + 40, primary.Work.Top + 40,
                Math.Min(previous.Right - previous.Left, primary.Work.Right - primary.Work.Left - 80),
                Math.Min(previous.Bottom - previous.Top, primary.Work.Bottom - primary.Work.Top - 80), 0x0014));
            Thread.Sleep(1200);
            Assert.Equal(192u, GetDpiForWindow(original.HostWindow));
            ((AutomationElement)original.NativeElement!).SetFocus();
            Thread.Sleep(800);
            output.WriteLine("monitor_transition: host_dpi=192; target=primary");
            RunOverlayRoundTrip(ParagraphFixtures, ComposerHost.Codex);
        }
        finally
        {
            Assert.True(SetWindowPos(original.HostWindow, nint.Zero, previous.Left, previous.Top,
                previous.Right - previous.Left, previous.Bottom - previous.Top, 0x0014));
            // Chromium applies its WM_DPICHANGED suggestion asynchronously. Restore the
            // original physical size once more after that transition has settled.
            Thread.Sleep(1200);
            Assert.True(SetWindowPos(original.HostWindow, nint.Zero, previous.Left, previous.Top,
                previous.Right - previous.Left, previous.Bottom - previous.Top, 0x0014));
        }
        Thread.Sleep(1200);
        Assert.Equal(168u, GetDpiForWindow(original.HostWindow));
        ((AutomationElement)original.NativeElement!).SetFocus();
        Thread.Sleep(800);
        output.WriteLine("monitor_transition: host_dpi=168; target=restored");
        RunOverlayRoundTrip(ParagraphFixtures, ComposerHost.Codex);
    }

    [Fact]
    public void PreparedSyntheticMultipleErrors_TenFirstAttemptRoundTrips()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_FIRST_ATTEMPT_STRESS_TEST") != "1") return;
        for (var round = 0; round < 10; round++)
        {
            RunNativeRoundTrip(MultipleErrorFixtures(), ComposerHost.Codex);
            output.WriteLine($"firstAttemptRound={round + 1}; correctedAndUndone=True");
        }
    }

    [Fact]
    public void PreparedSyntheticMultipleErrors_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_MULTI_ERROR_NATIVE_TEST") != "1") return;
        RunNativeRoundTrip(MultipleErrorFixtures(), ComposerHost.Codex);
    }

    [Fact]
    public void PreparedSyntheticMultipleErrors_IsCorrectedAndUndoneThroughOwnOverlay()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_MULTI_ERROR_LIVE_TEST") != "1") return;
        RunOverlayRoundTrip(MultipleErrorFixtures(), ComposerHost.Codex);
    }

    private static PreparedFixture[] MultipleErrorFixtures() =>
        (from suffix in new[] { "", "\n", "\r\n" }
         select new PreparedFixture(
             "Das ist ein fehler. Die katze sitzt auf dem tisch. Der hund liegt im garten." + suffix,
             "Das ist ein Fehler. Die Katze sitzt auf dem Tisch. Der Hund liegt im Garten." + suffix))
        .Concat(from marker in new[] { "• ", "- " }
            from newline in new[] { "\n", "\r\n" }
            from suffix in new[] { "", newline }
            select new PreparedFixture(
                string.Join(newline, Enumerable.Repeat(marker + "Das ist ein fehler. Die katze sitzt auf dem tisch.", 3)) + suffix,
                string.Join(newline, Enumerable.Repeat(marker + "Das ist ein Fehler. Die Katze sitzt auf dem Tisch.", 3)) + suffix)).ToArray();

    [Fact]
    public void PreparedSyntheticProtectedEnd_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_PROTECTED_END_NATIVE_TEST") != "1") return;
        RunNativeRoundTrip((from token in new[] { "https://example.com", "--output=result.txt", "`foo`" }
            from suffix in new[] { "", "\n", "\r\n" }
            select new PreparedFixture("Prüfe " + token + suffix, "Prüfe " + token + "." + suffix)).ToArray(), ComposerHost.Codex);
    }

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
    public void InstalledSyntheticComposer_RealAltGrShortcutUndoAndCleanup()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_INSTALLED_SHORTCUT_SMOKE") != "1") return;

        var targetWindowText = RequiredEnvironmentVariable("CODEX_LANGUAGE_FIX_INSTALLED_SHORTCUT_HWND");
        var targetWindow = ParseWindowHandle(targetWindowText);
        var appProcessId = int.Parse(
            RequiredEnvironmentVariable("CODEX_LANGUAGE_FIX_INSTALLED_SHORTCUT_PID"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var expectedExecutable = Path.GetFullPath(
            RequiredEnvironmentVariable("CODEX_LANGUAGE_FIX_INSTALLED_SHORTCUT_EXE"));

        Assert.True(IsWindow(targetWindow), "Das explizit angegebene Composer-Fenster ist nicht mehr vorhanden.");
        using var app = Process.GetProcessById(appProcessId);
        Assert.False(app.HasExited, "Der explizit angegebene Installationsprozess läuft nicht mehr.");
        Assert.Equal(expectedExecutable, Path.GetFullPath(app.MainModule?.FileName
            ?? throw new InvalidOperationException("Der Installationspfad des Zielprozesses ist nicht lesbar.")),
            StringComparer.OrdinalIgnoreCase);
        _ = GetWindowThreadProcessId(targetWindow, out var hostProcessId);
        Assert.True(hostProcessId > 0 && hostProcessId != appProcessId,
            "Das Ziel-HWND muss zum Composer-Host gehören und vom installierten Korrekturprozess verschieden sein.");
        Assert.Equal(targetWindow, GetForegroundWindow());

        var accessor = new CodexComposerAccessor();
        var original = CaptureTargetEmptyComposer(accessor, targetWindow, hostProcessId);
        Assert.True(IsEmptyObservedComposer(original.Text),
            "Der exakt fokussierte Composer muss leer sein.");
        Assert.Equal(CodexComposerAccessor.NativeInputSize, Marshal.SizeOf<KeyboardInputEvent>());

        var fixtureStates = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
        fixtureStates.Add(original.Text);
        for (var length = 1; length <= OriginalFixture.Length; length++)
            fixtureStates.Add(OriginalFixture[..length]);
        fixtureStates.UnionWith(PlannedStates(OriginalFixture, CorrectedFixture));

        var localizer = new AppLocalizer(new SettingsService().Load().Language);
        var fixtureInputStarted = false;
        var cleanupVerified = false;
        var correctionTimer = new Stopwatch();
        try
        {
            AssertGermanLayoutAndReleasedKeys();
            AssertTargetComposer(accessor, original, targetWindow, hostProcessId, original.Text);

            fixtureInputStarted = true;
            SendUnicodeText(OriginalFixture);
            WaitForComposerText(accessor, original, targetWindow, hostProcessId,
                OriginalFixture, fixtureStates, TimeSpan.FromSeconds(5));

            _ = WaitForButton(app, localizer.Get(AppText.CorrectAutomationName));
            AssertTargetComposer(accessor, original, targetWindow, hostProcessId, OriginalFixture);
            AssertGermanLayoutAndReleasedKeys();
            correctionTimer.Start();
            SendAltGrComma();
            WaitForComposerText(accessor, original, targetWindow, hostProcessId,
                CorrectedFixture, PlannedStates(OriginalFixture, CorrectedFixture), TimeSpan.FromSeconds(60));
            correctionTimer.Stop();

            var undo = WaitForButton(app, localizer.Get(AppText.Undo));
            AssertTargetComposer(accessor, original, targetWindow, hostProcessId, CorrectedFixture);
            ClickOwnButton(app, undo, targetWindow,
                () => AssertTargetComposer(accessor, original, targetWindow, hostProcessId, CorrectedFixture));
            WaitForComposerText(accessor, original, targetWindow, hostProcessId,
                OriginalFixture, PlannedStates(CorrectedFixture, OriginalFixture), TimeSpan.FromSeconds(10));
            AssertTargetComposer(accessor, original, targetWindow, hostProcessId, OriginalFixture);

            ClearKnownSyntheticText(accessor, original, targetWindow, hostProcessId, fixtureStates);
            cleanupVerified = true;
            output.WriteLine($"installed_shortcut_smoke: fixture_characters={OriginalFixture.Length}; trigger_to_write_ms={correctionTimer.Elapsed.TotalMilliseconds:F1}; cleanup_verified={cleanupVerified}");
        }
        finally
        {
            if (fixtureInputStarted && !cleanupVerified)
            {
                // Failure cleanup is limited to known prefixes/edit-plan states from this synthetic fixture.
                ClearKnownSyntheticText(accessor, original, targetWindow, hostProcessId, fixtureStates);
            }
        }
    }

    [Fact]
    public void PreparedSyntheticRepeatedBullets_DirectNativeRoundTrips()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_REPEATED_BULLETS_NATIVE_TEST") != "1") return;
        foreach (var correctedLine in new[] { "Das ist ein Fehler.", "Das ist eon fehler.",
            "Das ist ein fehler.!", "Dies ist ein Fehler." })
        foreach (var mask in new[] { 1, 2, 4, 7 })
        {
            RunNativeRoundTrip(RepeatedBulletFixtures(mask, correctedLine), ComposerHost.Codex);
        }
    }

    [Fact]
    public void PreparedSyntheticFiveBullets_DirectNativeRoundTrips()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_FIVE_BULLETS_NATIVE_TEST") != "1") return;
        foreach (var mask in new[] { 1, 2, 4, 8, 16, 31 })
            RunNativeRoundTrip(RepeatedBulletFixtures(mask, "Das ist ein Fehler.", 5), ComposerHost.Codex);
    }

    [Fact]
    public void PreparedSyntheticRepeatedBullets_IsCorrectedAndUndoneThroughOwnOverlay()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_REPEATED_BULLETS_LIVE_TEST") != "1") return;
        RunOverlayRoundTrip(RepeatedBulletFixtures(7, "Das ist ein Fehler.")
            .Concat(RepeatedBulletFixtures(31, "Das ist ein Fehler.", 5)).ToArray(), ComposerHost.Codex);
    }

    private static PreparedFixture[] RepeatedBulletFixtures(int mask, string correctedLine, int count = 3) =>
        (from marker in new[] { "• ", "- " }
         from newline in new[] { "\n", "\r\n" }
         from suffix in new[] { "", newline }
         select new PreparedFixture(
             string.Join(newline, Enumerable.Repeat(marker + "Das ist ein fehler.", count)) + suffix,
             string.Join(newline, Enumerable.Range(0, count).Select(index =>
                 marker + ((mask & (1 << index)) != 0 ? correctedLine : "Das ist ein fehler."))) + suffix)).ToArray();

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

    // Opt in with HERMES_DESKTOP_LIVE_TEST=1 after preparing the exact sentence fixture in Hermes.
    [Fact]
    public void PreparedSyntheticHermesSentence_IsCorrectedAndUndoneThroughOwnOverlay()
    {
        if (Environment.GetEnvironmentVariable("HERMES_DESKTOP_LIVE_TEST") != "1") return;
        RunOverlayRoundTrip(SentenceFixtures, ComposerHost.Hermes);
    }

    [Fact]
    public void PreparedSyntheticHermesParagraphList_IsCorrectedAndUndoneThroughOwnOverlay()
    {
        if (Environment.GetEnvironmentVariable("HERMES_DESKTOP_PARAGRAPH_LIVE_TEST") != "1") return;
        RunOverlayRoundTrip(ParagraphFixtures, ComposerHost.Hermes);
    }

    // Read-only discovery: CODEX_LANGUAGE_FIX_DESKTOP_FIXTURE_PROBE=1. Never authorizes a write or new fixture variant.
    [Fact]
    public void PreparedSyntheticParagraphList_ReadOnlyReportsVerifiedFixtureEncoding()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_DESKTOP_FIXTURE_PROBE") != "1") return;

        var snapshot = new CodexComposerAccessor().TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
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

    [Fact]
    public void PreparedSyntheticFocusedComposer_CorrectionCaptureAndRefreshSkipLayoutDiscovery()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_LAYOUT_FREE_CAPTURE_TEST") != "1") return;

        var accessor = new CodexComposerAccessor();
        var snapshot = accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
        Assert.True(snapshot is not null && snapshot.Host == ComposerHost.Codex,
            "Die vorbereitete Codex-Testeingabe ist nicht mit Tastaturfokus erreichbar.");
        Assert.Contains(SentenceFixtures.Concat(ParagraphFixtures), fixture => fixture.Original == snapshot!.Text);
        Assert.Equal(snapshot!.EditorBounds, snapshot.Bounds);
        Assert.Null(snapshot.RightControlBounds);
        Assert.Null(snapshot.OccupiedBounds);
        Assert.Null(snapshot.PlacementBounds);
        Assert.Null(snapshot.EditorTextBounds);
        Assert.Equal(0, accessor.LayoutCaptureCount);

        var refreshed = accessor.TryRefresh(snapshot);
        Assert.True(snapshot.SameEditor(refreshed) && refreshed!.Text == snapshot.Text,
            "Der frische Snapshot muss denselben beschreibbaren Testeditor enthalten.");
        Assert.Equal(0, accessor.LayoutCaptureCount);
    }

    private void RunOverlayRoundTrip(IReadOnlyList<PreparedFixture> fixtures, ComposerHost expectedHost)
    {
        using var dpiScope = new LiveDpiScope();
        var accessor = new CodexComposerAccessor();
        var (original, correctedText) = CapturePreparedFixture(accessor, fixtures, expectedHost, includeLayout: true);
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
            Assert.True(LiveDpiScope.IsPerMonitorV2(correctWindow), "Das installierte Overlay muss PerMonitorV2 verwenden.");
            var buttonBounds = correct.Current.BoundingRectangle;
            var scale = GetDpiForWindow(correctWindow) / 96d;
            Assert.InRange(buttonBounds.Width, 28 * scale - 1, 28 * scale + 1);
            Assert.InRange(buttonBounds.Height, 28 * scale - 1, 28 * scale + 1);
            Assert.True(original.PlacementBounds?.Contains(buttonBounds) == true,
                "Der tatsächliche Knopf muss innerhalb des geprüften Appfensters liegen.");
            var insideEditor = original.EditorBounds!.Value.IntersectsWith(buttonBounds);
            if (insideEditor)
            {
                Assert.Equal(ComposerHost.Codex, original.Host);
                Assert.NotEmpty(original.EditorTextBounds!);
                Assert.InRange(buttonBounds.Left, original.Bounds.Right - 32 * scale - 1,
                    original.Bounds.Right - 32 * scale + 1);
                Assert.InRange(buttonBounds.Top, original.Bounds.Top + 4 * scale - 1,
                    original.Bounds.Top + 4 * scale + 1);
            }
            var protectedObstacles = insideEditor
                ? (original.OccupiedBounds ?? []).Where(rect => rect != original.EditorBounds.Value).Concat(original.EditorTextBounds!)
                : (original.OccupiedBounds ?? []).Append(original.EditorBounds.Value);
            foreach (var obstacle in protectedObstacles)
            {
                var protectedBounds = obstacle;
                protectedBounds.Inflate(4 * scale - 1, 4 * scale - 1);
                Assert.False(protectedBounds.IntersectsWith(buttonBounds),
                    "Der tatsächliche Knopf muss Abstand zu Text und Bedienelementen halten.");
            }
            output.WriteLine(JsonSerializer.Serialize(new { buttonBounds, scale, insideEditor, perMonitorV2 = true }));
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
            var current = accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
            Assert.True(original.SameEditor(current) && current!.Text == expected,
                "Editoridentität oder synthetische Testeingabe hat sich vor der Aktion geändert.");
        }

        void WaitForText(string expected, IReadOnlySet<string> permittedStates)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(60))
            {
                var current = accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
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

    // Opt in with HERMES_DESKTOP_NATIVE_TEST=1; only the exact Hermes sentence fixture is authorized.
    [Fact]
    public void PreparedSyntheticHermesSentence_DirectNativeRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("HERMES_DESKTOP_NATIVE_TEST") != "1") return;
        RunNativeRoundTrip(SentenceFixtures, ComposerHost.Hermes);
    }

    private void RunNativeRoundTrip(IReadOnlyList<PreparedFixture> fixtures, ComposerHost expectedHost)
    {
        using var dpiScope = new LiveDpiScope();
        var accessor = new CodexComposerAccessor();
        var (original, correctedText) = CapturePreparedFixture(accessor, fixtures, expectedHost);
        var originalText = original.Text;
        output.WriteLine($"fixtureCharacters={originalText.Length}; lineBreaks={originalText.Count(c => c == '\n')}");

        var layoutCapturesBeforeWrite = accessor.LayoutCaptureCount;
        var applied = accessor.TryReplace(original, originalText, correctedText);
        Assert.Equal(layoutCapturesBeforeWrite, accessor.LayoutCaptureCount);
        if (!applied && original.NativeElement is AutomationElement native
            && original.SameEditor(accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true))
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
        Assert.Equal(layoutCapturesBeforeWrite, accessor.LayoutCaptureCount);
        var active = accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
        Assert.True(original.SameEditor(active) && active!.Text == correctedText,
            "Vor dem nativen Rückweg ist nicht mehr derselbe unveränderte Testeditor aktiv.");

        Assert.True(accessor.TryReplace(corrected!, correctedText, originalText), accessor.LastWriteStatus);
        Assert.Equal(layoutCapturesBeforeWrite, accessor.LayoutCaptureCount);
        var restored = accessor.TryRefresh(original);
        Assert.True(original.SameEditor(restored) && restored!.Text == originalText,
            "Der native Rückweg hat die synthetische Testeingabe nicht exakt wiederhergestellt.");
    }

    private static (ComposerSnapshot Original, string CorrectedText) CapturePreparedFixture(
        CodexComposerAccessor accessor,
        IReadOnlyList<PreparedFixture> fixtures,
        ComposerHost expectedHost,
        bool includeLayout = false)
    {
        var original = includeLayout
            ? accessor.TryPollFocusedComposer()
            : accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
        Assert.True(original is not null && original.Host == expectedHost,
            "Die vorbereitete Testeingabe ist nicht eindeutig im explizit erwarteten Host erreichbar.");
        Assert.Equal(includeLayout ? 1 : 0, accessor.LayoutCaptureCount);
        var fixture = fixtures.FirstOrDefault(candidate => candidate.Original == original!.Text);
        Assert.True(fixture is not null,
            "Der Live-Test darf ausschließlich eine vollständig übereinstimmende synthetische Fixturevariante bedienen.");
        return (original!, fixture!.Corrected);
    }

    private static string RequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Die explizite Zielangabe {name} fehlt.");
        return value!;
    }

    private static nint ParseWindowHandle(string value)
    {
        var handle = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
        Assert.True(handle > 0, "Das Composer-HWND muss ein positiver nativer Handle sein.");
        return (nint)handle;
    }

    private static bool IsEmptyObservedComposer(string text) => text.Length == 0
        || string.Equals(text.TrimEnd('\r', '\n'), "Mit Codex arbeiten", StringComparison.Ordinal);

    private static ComposerSnapshot CaptureTargetEmptyComposer(
        CodexComposerAccessor accessor,
        nint targetWindow,
        int hostProcessId)
    {
        Assert.Equal(targetWindow, GetForegroundWindow());
        var focused = AutomationElement.FocusedElement;
        Assert.NotNull(focused);
        Assert.True(focused!.Current.HasKeyboardFocus);
        Assert.Equal(hostProcessId, focused.Current.ProcessId);
        Assert.Equal(ComposerHost.Codex, ComposerHostProcess.Identify(focused.Current.ProcessId));
        Assert.True(CodexComposerAccessor.IsComposerShapeForHost(
            focused.Current.ControlType, focused.Current.ClassName, ComposerHost.Codex),
            "Das direkt fokussierte Element hat nicht die bekannte Codex-Composer-Form.");

        var snapshot = accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
        if (snapshot is null && accessor.LastCaptureStatus == "ambiguous_placeholder")
            snapshot = CaptureConfirmedEmptyPlaceholder(accessor);

        Assert.NotNull(snapshot);
        Assert.Equal(ComposerHost.Codex, snapshot!.Host);
        Assert.Equal("chat", snapshot.FieldCategory);
        Assert.Equal(targetWindow, snapshot.HostWindow);
        Assert.Equal(hostProcessId, snapshot.ProcessId);
        Assert.NotEmpty(snapshot.RuntimeId);
        Assert.True(IsEmptyObservedComposer(snapshot.Text),
            "Der fokussierte Composer enthält keine leere Eingabe.");
        return snapshot;
    }

    private static ComposerSnapshot? CaptureConfirmedEmptyPlaceholder(CodexComposerAccessor accessor)
    {
        var focused = AutomationElement.FocusedElement;
        if (focused is null || !focused.Current.HasKeyboardFocus
            || !string.Equals(focused.Current.Name, "Mit Codex arbeiten", StringComparison.Ordinal)
            || ComposerHostProcess.Identify(focused.Current.ProcessId) != ComposerHost.Codex
            || !CodexComposerAccessor.IsComposerShapeForHost(
                focused.Current.ControlType, focused.Current.ClassName, ComposerHost.Codex))
            return null;

        var createSnapshot = typeof(CodexComposerAccessor).GetMethod(
            "CreateSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        if (createSnapshot is null) return null;
        var snapshot = createSnapshot.Invoke(accessor, [focused, ComposerHost.Codex, false, false]) as ComposerSnapshot;
        return snapshot is not null && IsEmptyObservedComposer(snapshot.Text) ? snapshot : null;
    }

    private static ComposerSnapshot CaptureTargetComposer(
        CodexComposerAccessor accessor,
        ComposerSnapshot expected,
        nint targetWindow,
        int hostProcessId)
    {
        Assert.Equal(targetWindow, GetForegroundWindow());
        var current = accessor.TryCaptureFocusedComposerWithoutLayout(requireKeyboardFocus: true);
        if (current is null && accessor.LastCaptureStatus == "ambiguous_placeholder")
            current = CaptureConfirmedEmptyPlaceholder(accessor);

        Assert.True(expected.SameEditor(current), "Der ursprüngliche fokussierte Composer ist nicht mehr aktiv.");
        Assert.Equal(ComposerHost.Codex, current!.Host);
        Assert.Equal("chat", current.FieldCategory);
        Assert.Equal(targetWindow, current.HostWindow);
        Assert.Equal(hostProcessId, current.ProcessId);
        return current;
    }

    private static void AssertTargetComposer(
        CodexComposerAccessor accessor,
        ComposerSnapshot expected,
        nint targetWindow,
        int hostProcessId,
        string expectedText)
    {
        var current = CaptureTargetComposer(accessor, expected, targetWindow, hostProcessId);
        Assert.Equal(expectedText, current.Text);
    }

    private static void WaitForComposerText(
        CodexComposerAccessor accessor,
        ComposerSnapshot expected,
        nint targetWindow,
        int hostProcessId,
        string expectedText,
        IReadOnlySet<string> permittedStates,
        TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var current = CaptureTargetComposer(accessor, expected, targetWindow, hostProcessId);
            if (string.Equals(current.Text, expectedText, StringComparison.Ordinal)) return;
            Assert.Contains(current.Text, permittedStates);
            Thread.Sleep(50);
        }

        Assert.Fail("Der Composer hat den erwarteten synthetischen Zustand nicht rechtzeitig erreicht.");
    }

    private static void ClearKnownSyntheticText(
        CodexComposerAccessor accessor,
        ComposerSnapshot expected,
        nint targetWindow,
        int hostProcessId,
        IReadOnlySet<string> permittedSyntheticStates)
    {
        var current = CaptureTargetComposer(accessor, expected, targetWindow, hostProcessId);
        if (IsEmptyObservedComposer(current.Text)) return;
        Assert.Contains(current.Text, permittedSyntheticStates);
        AssertGermanLayoutAndReleasedKeys();

        SendSelectAllBackspace();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            current = CaptureTargetComposer(accessor, expected, targetWindow, hostProcessId);
            if (IsEmptyObservedComposer(current.Text)) return;
            Assert.Contains(current.Text, permittedSyntheticStates);
            Thread.Sleep(50);
        }

        Assert.Fail("Die bekannte synthetische Eingabe ließ sich nicht verifiziert entfernen.");
    }

    private static void AssertGermanLayoutAndReleasedKeys()
    {
        var language = unchecked((ushort)(GetKeyboardLayout(0).ToInt64() & 0xFFFF));
        Assert.Equal(0x0007, language & 0x03FF);
        foreach (var key in new ushort[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xBC })
            Assert.Equal((short)0, (short)(GetAsyncKeyState(key) & 0x8000));
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = text.SelectMany(character => new[]
        {
            KeyboardInput(0, character, 0x0004),
            KeyboardInput(0, character, 0x0004 | 0x0002)
        }).ToArray();
        SendKeyboardInputExactly(inputs, "synthetic_fixture_input_failed");
    }

    private static void SendAltGrComma()
    {
        KeyboardInputEvent[] inputs = [
            KeyboardInput(0xA5, 0, 0x0001),
            KeyboardInput(0xBC, 0, 0),
            KeyboardInput(0xBC, 0, 0x0002),
            KeyboardInput(0xA5, 0, 0x0001 | 0x0002)];
        SendKeyboardInputExactly(inputs, "installed_altgr_shortcut_input_failed");
    }

    private static void SendSelectAllBackspace()
    {
        KeyboardInputEvent[] inputs = [
            KeyboardInput(0xA2, 0, 0),
            KeyboardInput(0x41, 0, 0),
            KeyboardInput(0x41, 0, 0x0002),
            KeyboardInput(0xA2, 0, 0x0002),
            KeyboardInput(0x08, 0, 0),
            KeyboardInput(0x08, 0, 0x0002)];
        SendKeyboardInputExactly(inputs, "guarded_synthetic_cleanup_input_failed");
    }

    private static KeyboardInputEvent KeyboardInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = 1,
        Data = new KeyboardInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags
            }
        }
    };

    private static void SendKeyboardInputExactly(KeyboardInputEvent[] inputs, string failureMessage)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyboardInputEvent>()) == (uint)inputs.Length) return;
        ReleaseInjectedKeyboardKeys();
        Assert.Fail(failureMessage);
    }

    private static void ReleaseInjectedKeyboardKeys()
    {
        KeyboardInputEvent[] releases = [
            KeyboardInput(0xA5, 0, 0x0001 | 0x0002),
            KeyboardInput(0xBC, 0, 0x0002),
            KeyboardInput(0xA2, 0, 0x0002),
            KeyboardInput(0x08, 0, 0x0002),
            KeyboardInput(0x41, 0, 0x0002)];
        _ = SendInput((uint)releases.Length, releases, Marshal.SizeOf<KeyboardInputEvent>());
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
    private static extern uint GetDpiForWindow(nint window);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, KeyboardInputEvent[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(ushort virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y,
        int width, int height, uint flags);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputEvent
    {
        public uint Type;
        public KeyboardInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct KeyboardInputUnion
    {
        [FieldOffset(0)] public NativeMouseInput Mouse;
        [FieldOffset(0)] public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
