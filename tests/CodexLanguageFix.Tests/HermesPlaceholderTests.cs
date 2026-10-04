using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class HermesPlaceholderTests
{
    [Theory]
    [InlineData("What are we building?")]
    [InlineData("Give Hermes a task")]
    [InlineData("What's on your mind?")]
    [InlineData("Describe what you need")]
    [InlineData("What should we tackle?")]
    [InlineData("Ask anything")]
    [InlineData("Start with a goal")]
    [InlineData("Was bauen wir?")]
    [InlineData("Geben Sie Hermes eine Aufgabe")]
    [InlineData("Was ist Ihnen wichtig?")]
    [InlineData("Beschreiben Sie, was Sie brauchen")]
    [InlineData("Was sollen wir angehen?")]
    [InlineData("Fragen Sie irgendetwas")]
    [InlineData("Beginnen Sie mit einem Ziel")]
    [InlineData("Send a follow-up")]
    [InlineData("Add more context")]
    [InlineData("Refine the request")]
    [InlineData("What's next?")]
    [InlineData("Keep it going")]
    [InlineData("Push it further")]
    [InlineData("Adjust or continue")]
    [InlineData("Folge senden")]
    [InlineData("Mehr Kontext hinzufügen")]
    [InlineData("Anfrage verfeinern")]
    [InlineData("Was kommt als Nächstes?")]
    [InlineData("Weiter so")]
    [InlineData("Noch weiter")]
    [InlineData("Anpassen oder fortfahren")]
    [InlineData("Starting Hermes...")]
    [InlineData("Reconnecting to Hermes…")]
    [InlineData("Hermes wird gestartet…")]
    [InlineData("Verbindung zu Hermes wird wiederhergestellt…")]
    [InlineData("Edit message")]
    [InlineData("Nachricht bearbeiten")]
    public void KnownLocalizedHermesChatPlaceholder_IsAmbiguous(string text)
    {
        Assert.True(HermesPlaceholder.IsAmbiguous(text));
    }

    [Theory]
    [InlineData("What are we building?\n")]
    [InlineData("Was bauen wir?\r\n")]
    [InlineData("Nachricht bearbeiten\n\r\n")]
    public void TrailingTextPatternNewlines_AreIgnored(string text)
    {
        Assert.True(HermesPlaceholder.IsAmbiguous(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("What are we building? ")]
    [InlineData(" What are we building?")]
    [InlineData("what are we building?")]
    [InlineData("What are we building?\t")]
    [InlineData("Please help me write something.")]
    [InlineData("Send a follow up")]
    public void NonExactOrOrdinaryText_IsNotAmbiguous(string? text)
    {
        Assert.False(HermesPlaceholder.IsAmbiguous(text));
    }
}
