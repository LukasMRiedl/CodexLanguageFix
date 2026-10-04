using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class NaturalLanguageFieldTests
{
    [Theory]
    [InlineData("Leg einfach los", "Leg einfach los\n", true)]
    [InlineData("Get started", "Get started\r\n", true)]
    [InlineData("Leg einfach los", "Das ist ein fehler.\n", false)]
    [InlineData("Leg einfach los", " Leg einfach los", false)]
    [InlineData("Leg einfach los", "Leg einfach los weiter", false)]
    [InlineData("", "Das ist ein fehler.", false)]
    public void CodexComposerHintRequiresExactNonemptyAccessibleName(string name, string text, bool expected)
    {
        Assert.Equal(expected, NaturalLanguageField.IsAmbiguousComposerHint(ComposerHost.Codex, name, text));
        Assert.False(NaturalLanguageField.IsAmbiguousComposerHint(ComposerHost.Antigravity, name, text));
    }

    [Theory]
    [InlineData("artifact-feedback-details")]
    [InlineData("chatgpt-custom-instructions")]
    [InlineData("chatgpt-personalization-more-about-you")]
    public void CodexSourceVerifiedFieldIdsIdentifyNaturalLanguageFields(string automationId)
    {
        Assert.True(NaturalLanguageField.IsSupported(
            ComposerHost.Codex, "textarea", automationId, "Text", null));
    }

    [Theory]
    [InlineData("Custom instructions")]
    [InlineData("More about you")]
    [InlineData("How else could we improve? (optional)")]
    [InlineData("Benutzerdefinierte Anweisungen")]
    [InlineData("Mehr über dich")]
    [InlineData("Was könnten wir sonst noch verbessern? (optional)")]
    [InlineData("Edit message")]
    [InlineData("Nachricht bearbeiten")]
    public void CodexExactLabelsIdentifyNaturalLanguageFields(string label)
    {
        Assert.True(NaturalLanguageField.IsSupported(
            ComposerHost.Codex, "textarea", null, "Text", label));
    }

    [Theory]
    [InlineData("cron-prompt", "Text", "Prompt")]
    [InlineData("webhook-prompt", "Text", "Prompt")]
    [InlineData("new-profile-soul", "Text", "SOUL.md")]
    public void HermesTrustedAutomationIdsIdentifyNaturalLanguageFields(
        string automationId, string name, string label)
    {
        Assert.True(NaturalLanguageField.IsSupported(
            ComposerHost.Hermes, "TextBox", automationId, name, label));
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("Beschreibung")]
    [InlineData("What should this bot help with?")]
    [InlineData("SOUL.md bearbeiten…")]
    [InlineData("Kriterium")]
    [InlineData("Idee")]
    [InlineData("Sonstiges")]
    [InlineData("Edit message")]
    [InlineData("Nachricht bearbeiten")]
    public void HermesExactLocalizedLabelsIdentifyProseFields(string label)
    {
        Assert.True(NaturalLanguageField.IsSupported(
            ComposerHost.Hermes, "TextBox", null, "Text", label));
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Title")]
    [InlineData("Search")]
    [InlineData("Path")]
    [InlineData("URL")]
    [InlineData("API key")]
    [InlineData("Password")]
    [InlineData("Code")]
    [InlineData("JSON")]
    [InlineData("Settings")]
    [InlineData("Configuration")]
    public void TechnicalAndSensitiveLabelsAreExcluded(string label)
    {
        Assert.False(NaturalLanguageField.IsSupported(
            ComposerHost.Hermes, "TextBox", null, label, label));
    }

    [Theory]
    [InlineData("Nickname")]
    [InlineData("Occupation")]
    [InlineData("Project name")]
    [InlineData("Password")]
    public void CodexPersonalMetadataAndSensitiveFieldsAreExcluded(string label)
    {
        Assert.False(NaturalLanguageField.IsSupported(
            ComposerHost.Codex, "textarea", null, label, label));
    }

    [Theory]
    [InlineData("chatgpt-personalization-nickname", "More about you")]
    [InlineData("chatgpt-personalization-occupation", "Custom instructions")]
    [InlineData("chatgpt-project-name", "Custom instructions")]
    public void CodexNegativeContextOverridesPositiveLabels(string automationId, string label)
    {
        Assert.False(NaturalLanguageField.IsSupported(
            ComposerHost.Codex, "textarea", automationId, "Text", label));
    }

    [Theory]
    [InlineData("cron-name", "Prompt")]
    [InlineData("apiKeyInput", "Prompt")]
    [InlineData("json-config", "Description")]
    public void NegativeContextOverridesPositiveLabel(string automationId, string label)
    {
        Assert.False(NaturalLanguageField.IsSupported(
            ComposerHost.Hermes, "TextBox", automationId, label, label));
    }

    [Fact]
    public void ClassTokensAndArbitrarySubstringsDoNotQualifyAField()
    {
        Assert.False(NaturalLanguageField.IsSupported(
            ComposerHost.Hermes, "cursor-text overflow-y-auto", null,
            "Prompt Settings", "Description of JSON"));
        Assert.False(NaturalLanguageField.IsSupported(
            ComposerHost.Hermes, "ProseMirror", null, null, null));
    }

    [Theory]
    [InlineData(ComposerHost.Codex)]
    [InlineData(ComposerHost.Antigravity)]
    public void OtherHostsNeedTheirOwnVerifiedFieldRule(ComposerHost host)
    {
        Assert.False(NaturalLanguageField.IsSupported(host, "TextBox", "cron-prompt", "Prompt", "Prompt"));
    }

    [Fact]
    public void CodeEditorClassBlocksEvenAnOtherwisePositiveLabel()
    {
        Assert.False(NaturalLanguageField.IsSupported(
            ComposerHost.Hermes, "monaco-editor", "new-profile-soul", "SOUL.md", "SOUL.md"));
    }
}
