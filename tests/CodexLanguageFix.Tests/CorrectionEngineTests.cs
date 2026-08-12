using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class CorrectionEngineTests
{
    private readonly CorrectionEngine _engine = new();

    [Fact]
    public void Annotate_ProtectsMarkdownAndTechnicalTokens()
    {
        const string text = "Bitte prüfe `var eror = 1;`, https://example.test/falsch und C:\\Temp\\eror.txt.\n```csharp\nvar mistkae = 1;\n```\nDer Satz ist korekt.";

        var prompt = _engine.Annotate(text);

        Assert.Contains(prompt.ProtectedSpans, span => text.Substring(span.Start, span.Length).Contains("var eror"));
        Assert.Contains(prompt.ProtectedSpans, span => text.Substring(span.Start, span.Length).StartsWith("https://", StringComparison.Ordinal));
        Assert.Contains(prompt.ProtectedSpans, span => text.Substring(span.Start, span.Length).StartsWith("C:\\", StringComparison.Ordinal));
        Assert.Contains(prompt.ProtectedSpans, span => text.Substring(span.Start, span.Length).Contains("mistkae"));
        Assert.DoesNotContain(prompt.ProtectedSpans, span => text.Substring(span.Start, span.Length).Contains("korekt"));
    }

    [Fact]
    public void Apply_NeverChangesProtectedSpan()
    {
        const string text = "Nutze `eror` und schreibe korekt.";
        var prompt = _engine.Annotate(text);
        var protectedOffset = text.IndexOf("eror", StringComparison.Ordinal);
        var proseOffset = text.IndexOf("korekt", StringComparison.Ordinal);
        var matches = new[]
        {
            Match(protectedOffset, 4, "error", 0, 0.99),
            Match(proseOffset, 6, "korrekt", 1, 0.55)
        };

        var result = _engine.Apply(text, prompt, matches);

        Assert.Equal("Nutze `eror` und schreibe korrekt.", result.CorrectedText);
        Assert.Single(result.Corrections);
    }

    [Fact]
    public void Apply_UsesConfidenceThenLengthThenApiOrderForOverlaps()
    {
        const string text = "abcdef";
        var prompt = _engine.Annotate(text);
        var matches = new[]
        {
            Match(1, 3, "X", 0, 0.50),
            Match(2, 2, "Y", 1, 0.90),
            Match(4, 2, "Z", 2, 0.40)
        };

        var result = _engine.Apply(text, prompt, matches);

        Assert.Equal("abYZ", result.CorrectedText);
        Assert.Equal(2, result.Corrections.Count);
    }

    [Fact]
    public void Apply_PreservesUtf16OffsetsIncludingEmojiAndUmlauts()
    {
        const string text = "🙂 Die Änderug ist korekt.";
        var prompt = _engine.Annotate(text);
        var matches = new[]
        {
            Match(text.IndexOf("Änderug", StringComparison.Ordinal), "Änderug".Length, "Änderung", 0),
            Match(text.IndexOf("korekt", StringComparison.Ordinal), "korekt".Length, "korrekt", 1)
        };

        var result = _engine.Apply(text, prompt, matches);

        Assert.Equal("🙂 Die Änderung ist korrekt.", result.CorrectedText);
    }

    [Fact]
    public void Apply_HandlesInsertionsAndLineBreaks()
    {
        const string text = "Wenn es klappt\nstarten wir.";
        var prompt = _engine.Annotate(text);
        var insertion = text.IndexOf('\n');

        var result = _engine.Apply(text, prompt, [Match(insertion, 0, ",", 0)]);

        Assert.Equal("Wenn es klappt,\nstarten wir.", result.CorrectedText);
    }

    private static LanguageToolMatch Match(int offset, int length, string replacement, int index, double? confidence = 0.5) =>
        new(offset, length, [replacement], $"RULE_{index}", "TEST", "test", confidence, index);
}
