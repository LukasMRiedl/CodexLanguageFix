using System.Text.Json;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class TextStructureTests
{
    [Theory]
    [InlineData("  Das ist korekt.  ", "Das ist korrekt.", "  Das ist korrekt.  ")]
    [InlineData("Das ist korekt.", "  Das ist korrekt.\t", "Das ist korrekt.")]
    [InlineData("\t- korekt  \r\n\r\n  - Zweiter\t\r\n", "- korrekt\n- Zweiter", "\t- korrekt  \r\n\r\n  - Zweiter\t\r\n")]
    [InlineData("Erster\r\n \t\r\nZweiter\r\n", "\nErster\n\n\nZweiter\n\n", "Erster\r\n \t\r\nZweiter\r\n")]
    [InlineData("  > - [ ] korekt  \n", ">   -  [ ] korrekt", "  > - [ ] korrekt  \n")]
    [InlineData("1. korekt\r2. Zweiter", "  1. korrekt\r\n  2. Zweiter", "1. korrekt\r2. Zweiter")]
    [InlineData("\u00a0Grüße 🙂\t", "Grüße 🙂", "\u00a0Grüße 🙂\t")]
    [InlineData("- \n", " -  ", "- \n")]
    public void RestoresOnlyOriginalWhitespaceLayout(string original, string corrected, string expected)
    {
        Assert.True(TextStructure.TryRestoreLayout(original, corrected, out var restored));
        Assert.Equal(expected, restored);
        Assert.True(TextStructure.IsPreserved(original, restored));
    }

    [Theory]
    [InlineData("Text", "")]
    [InlineData("Text", " \r\n\t")]
    [InlineData("Erster\nZweiter", "Erster Zweiter")]
    [InlineData("Erster Zweiter", "Erster\nZweiter")]
    [InlineData("Erster\nZweiter", "Erster\n")]
    [InlineData("- Punkt\n- Zweiter", "Punkt\n- Zweiter")]
    [InlineData("• Punkt", "- Punkt")]
    [InlineData("1. Punkt", "2. Punkt")]
    [InlineData("1. Punkt", "1) Punkt")]
    [InlineData("- [ ] Punkt", "- [x] Punkt")]
    [InlineData("> ## Titel", "## Titel")]
    [InlineData("Text", "- Text")]
    [InlineData("- Text", "- ")]
    [InlineData("- \n", "\n")]
    public void LayoutRestorationCannotHideMissingContentOrChangedStructure(string original, string corrected)
    {
        Assert.False(TextStructure.TryRestoreLayout(original, corrected, out var restored));
        Assert.Empty(restored);
    }

    [Theory]
    [InlineData("Text\n\nAbsatz", "Text Absatz")]
    [InlineData("Text\r\nAbsatz", "Text\nAbsatz")]
    [InlineData("Text\rAbsatz", "Text\nAbsatz")]
    [InlineData("- Punkt\n- Zweiter", "Punkt\n- Zweiter")]
    [InlineData("  - Punkt", "- Punkt")]
    [InlineData("1. Punkt", "2. Punkt")]
    [InlineData("- [ ] Punkt", "- [x] Punkt")]
    [InlineData("> ## Titel", "## Titel")]
    [InlineData("Text  \nAbsatz", "Text\nAbsatz")]
    [InlineData("Text\n \t\n", "Text\n\n")]
    [InlineData("Text\n", "Text")]
    [InlineData("Text", "")]
    [InlineData("- Text", "- ")]
    [InlineData("Text\nZweiter", "Text\n")]
    [InlineData("Text", "Text\n")]
    [InlineData("Text", "- Text")]
    [InlineData("• Text", "◦ Text")]
    [InlineData("\u00a0Text", "Text")]
    public void RejectsStructureChanges(string original, string corrected) =>
        Assert.False(TextStructure.IsPreserved(original, corrected));

    [Theory]
    [InlineData("Wenn es klappt\nstarten wir.", "Wenn es klappt,\nstarten wir.")]
    [InlineData("- Punkt ist korekt.\r\n  2) Änderug 🙂\r\n\r\n", "- Punkt ist korrekt.\r\n  2) Änderung 🙂\r\n\r\n")]
    [InlineData("> ## Ändern\n> - [ ] korekt  \n", "> ## Ändern\n> - [ ] korrekt  \n")]
    [InlineData("- ", "- ")]
    [InlineData("\n \t\n", "\n \t\n")]
    [InlineData("", "")]
    public void AllowsCorrectionsWithinStructure(string original, string corrected) =>
        Assert.True(TextStructure.IsPreserved(original, corrected));

    [Fact]
    public void ReconstructsLinesByteExactlyAndHandlesMaximumLength()
    {
        const string prefix = "\t> - [ ] Änderug 🙂  \r\n\n```csharp\rvar x = 1;\n```\nhttps://example.test C:\\Temp\\file.txt\n";
        var original = prefix + new string('a', 20_000 - prefix.Length);
        var lines = TextStructure.GetLines(original);

        Assert.Equal(original, string.Concat(lines.Select(line => line.Content + line.LineEnding)));
        Assert.Equal("\t> - [ ] ", lines[0].Content[..lines[0].PrefixLength]);
        Assert.Equal(2, lines[0].TrailingWhitespaceLength);
        Assert.True(TextStructure.IsPreserved(original, original.Replace("Änderug", "Änderung", StringComparison.Ordinal)));
    }

    [Fact]
    public void LanguageToolDropsDestructiveEditsAndKeepsSafeCorrections()
    {
        const string text = "- korekt\r\n- Zweiter\r\n\r\n";
        var engine = new CorrectionEngine();
        var result = engine.Apply(text, engine.Annotate(text),
        [
            Match(0, 2, "", 0),
            Match(text.IndexOf("\r\n", StringComparison.Ordinal), 2, " ", 1),
            Match(2, 6, "korrekt", 2)
        ]);

        Assert.Equal("- korrekt\r\n- Zweiter\r\n\r\n", result.CorrectedText);
        Assert.Single(result.Corrections);
    }

    [Fact]
    public void LanguageToolCannotRemoveWholeContentThroughMultipleEdits()
    {
        const string text = "ab";
        var engine = new CorrectionEngine();
        var result = engine.Apply(text, engine.Annotate(text), [Match(0, 1, "", 0), Match(1, 1, "", 1)]);

        Assert.NotEmpty(result.CorrectedText);
        Assert.Single(result.Corrections);
    }

    [Fact]
    public void LanguageToolIgnoresOverflowingProviderOffsets()
    {
        var engine = new CorrectionEngine();
        var result = engine.Apply("Text", engine.Annotate("Text"), [Match(int.MaxValue, int.MaxValue, "", 0)]);

        Assert.Equal("Text", result.CorrectedText);
        Assert.Empty(result.Corrections);
    }

    [Theory]
    [InlineData("")]
    [InlineData("- korrekt - Zweiter")]
    [InlineData("korrekt\n- Zweiter")]
    public async Task LunaRejectsDestructiveResponsesBeforeCaching(string output)
    {
        var client = new ResponseClient(output);
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<LanguageFixException>(() =>
                provider.CorrectAsync("- korekt\n- Zweiter", CancellationToken.None));
        }

        Assert.Equal(2, client.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LunaRejectsDestructiveExperimentalProtocolBeforeCaching(bool spans)
    {
        var client = new ResponseClient("- ");
        var provider = new LunaCorrectionProvider(client, new AppLocalizer("de"), true, 0,
            spans ? LunaOutputProtocol.SpanEdits : LunaOutputProtocol.Segments, fallbackEnabled: false);

        await Assert.ThrowsAsync<LanguageFixException>(() =>
            provider.CorrectAsync("- korekt", CancellationToken.None));
    }

    private static LanguageToolMatch Match(int offset, int length, string replacement, int index) =>
        new(offset, length, [replacement], $"RULE_{index}", "TEST", "test", 0.5, index);

    private sealed class ResponseClient(string output) : ICodexAppServerClient, IStructuredCodexCorrectionClient
    {
        public int Calls { get; private set; }
        public Task<CodexAccountState> GetAccountAsync(CancellationToken token) => Task.FromResult(new CodexAccountState(true, true));
        public Task ConnectChatGptAsync(CancellationToken token) => Task.CompletedTask;
        public Task<bool> SupportsLunaAsync(CancellationToken token) => Task.FromResult(true);
        public Task<string> RunCorrectionAsync(string text, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(JsonSerializer.Serialize(new { corrected_text = output }));
        }

        public Task<string> RunStructuredCorrectionAsync(string inputJson, JsonElement schema,
            string instructions, string effort, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(schema.GetProperty("properties").TryGetProperty("edits", out _)
                ? JsonSerializer.Serialize(new { edits = new[] { new { start = 2, length = 6, text = "" } } })
                : JsonSerializer.Serialize(new { changes = new[] { new { id = 0, text = output } } }));
        }

        public void Dispose() { }
    }
}
