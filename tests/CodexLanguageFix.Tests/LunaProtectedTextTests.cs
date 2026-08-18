using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class LunaProtectedTextTests
{
    [Fact]
    public void Create_ReplacesTechnicalContentAndRestoresItExactly()
    {
        const string original = "Bitte prüfe `var eror = 1;`, https://example.test/falsch und C:\\Temp\\eror.txt. Der Satz ist korekt.";
        var protectedText = LunaProtectedText.Create(original);

        Assert.DoesNotContain("var eror", protectedText.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", protectedText.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Temp", protectedText.Text, StringComparison.Ordinal);
        Assert.Contains("korekt", protectedText.Text, StringComparison.Ordinal);

        var corrected = protectedText.Text.Replace("korekt", "korrekt", StringComparison.Ordinal);
        Assert.True(protectedText.TryRestore(corrected, out var restored));
        Assert.Equal(original.Replace("korekt", "korrekt", StringComparison.Ordinal), restored);
    }

    [Fact]
    public void Restore_RejectsMissingDuplicatedReorderedAndUnknownPlaceholders()
    {
        var protectedText = LunaProtectedText.Create("Nutze `one` und `two`.");
        var placeholders = protectedText.Text.Split(' ').Where(value => value.Contains("CLF_PROTECTED_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, placeholders.Length);

        Assert.False(protectedText.TryRestore(protectedText.Text.Replace(placeholders[0], string.Empty, StringComparison.Ordinal), out _));
        Assert.False(protectedText.TryRestore(protectedText.Text + placeholders[0], out _));
        Assert.False(protectedText.TryRestore(protectedText.Text.Replace(placeholders[0], placeholders[1], StringComparison.Ordinal), out _));
        Assert.False(protectedText.TryRestore(protectedText.Text + " ⟦CLF_PROTECTED_DEADBEEF_9999⟧", out _));
    }

    [Fact]
    public void Restore_WithNoProtectedContentPreservesCorrectedText()
    {
        var protectedText = LunaProtectedText.Create("Das ist korekt.");
        Assert.True(protectedText.TryRestore("Das ist korrekt.", out var restored));
        Assert.Equal("Das ist korrekt.", restored);
    }
}
