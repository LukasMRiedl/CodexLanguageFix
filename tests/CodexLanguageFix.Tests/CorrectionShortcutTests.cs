using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class CorrectionShortcutTests
{
    [Theory]
    [InlineData(3u, 0xBCu, true)]
    [InlineData(2u, 0x51u, true)]
    [InlineData(5u, 0xBAu, true)]
    [InlineData(4u, 0x70u, true)]
    [InlineData(0u, 0x87u, true)]
    [InlineData(2u, 0x70u, true)]
    [InlineData(0u, 0x70u, true)]
    [InlineData(0u, 0x51u, false)]
    [InlineData(4u, 0x51u, false)]
    [InlineData(8u, 0x51u, false)]
    [InlineData(0x4000u, 0x51u, false)]
    [InlineData(3u, 0x11u, false)]
    [InlineData(3u, 0x5Bu, false)]
    [InlineData(3u, 0x7Bu, false)]
    [InlineData(3u, 0xE5u, false)]
    [InlineData(3u, 0xE7u, false)]
    [InlineData(3u, 0u, false)]
    [InlineData(3u, 7u, false)]
    [InlineData(3u, 0xFFu, false)]
    [InlineData(3u, 0x100u, false)]
    public void IsValid_EnforcesSupportedModifierAndKeyCombinations(
        uint modifiers,
        uint virtualKey,
        bool expected) =>
        Assert.Equal(expected, new CorrectionShortcut(modifiers, virtualKey).IsValid);

    [Fact]
    public void Default_IsAltGrComma()
    {
        Assert.Equal(new CorrectionShortcut(3, 0xBC), CorrectionShortcut.Default);
        Assert.True(CorrectionShortcut.Default.IsValid);
    }

    [Fact]
    public void DisplayText_LocalizesModifiersAndUsesKeyboardLayoutForKeyName()
    {
        var shortcut = new CorrectionShortcut(7, 0xBC);

        var german = shortcut.GetDisplayText(new AppLocalizer(AppLocalizer.German));
        var english = shortcut.GetDisplayText(new AppLocalizer(AppLocalizer.English));

        Assert.StartsWith("Strg+Alt+Umschalt+", german, StringComparison.Ordinal);
        Assert.StartsWith("Ctrl+Alt+Shift+", english, StringComparison.Ordinal);
        Assert.True(german.Length > "Strg+Alt+Umschalt+".Length);
        Assert.True(english.Length > "Ctrl+Alt+Shift+".Length);
    }

    [Fact]
    public void FunctionKeyDisplayUsesFNumber()
    {
        var display = new CorrectionShortcut(4, 0x87)
            .GetDisplayText(new AppLocalizer(AppLocalizer.English));

        Assert.Equal("Shift+F24", display);
    }
}
