using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class ProtectedSpanDetectorTests
{
    [Fact]
    public void Detect_ProtectsMarkdownAndTechnicalTokens()
    {
        const string text = "Bitte prüfe `var eror = 1;`, https://example.test/falsch und C:\\Temp\\eror.txt.\n```csharp\nvar mistkae = 1;\n```\nDer Satz ist korekt.";

        var spans = ProtectedSpanDetector.Detect(text);

        Assert.Contains(spans, span => text.Substring(span.Start, span.Length).Contains("var eror"));
        Assert.Contains(spans, span => text.Substring(span.Start, span.Length).StartsWith("https://", StringComparison.Ordinal));
        Assert.Contains(spans, span => text.Substring(span.Start, span.Length).StartsWith("C:\\", StringComparison.Ordinal));
        Assert.Contains(spans, span => text.Substring(span.Start, span.Length).Contains("mistkae"));
        Assert.DoesNotContain(spans, span => text.Substring(span.Start, span.Length).Contains("korekt"));
    }

}
