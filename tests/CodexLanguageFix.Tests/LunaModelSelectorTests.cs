using System.Text.Json;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaModelSelectorTests
{
    [Fact]
    public void Select_ChoosesHighestSupportedVersionAcrossPages()
    {
        var pages = new[]
        {
            Page("""
                {"data":[
                  {"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]},
                  {"model":"gpt-6.2-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}
                ]}
                """),
            Page("""
                {"data":[
                  {"model":"gpt-6.10-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}
                ]}
                """)
        };

        var selection = LunaModelSelector.Select(pages);

        Assert.Equal(new LunaModelSelection("gpt-6.10-luna", "low", "priority"), selection);
    }

    [Fact]
    public void Select_ComparesMajorVersionNumerically()
    {
        var page = Page("""
            {"data":[
              {"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-6-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}
            ]}
            """);

        Assert.Equal("gpt-6-luna", LunaModelSelector.Select([page])?.Model);
    }

    [Fact]
    public void Select_RequiresBothOfficialCapabilitiesAndFallsBackToOlderSupportedModel()
    {
        var page = Page("""
            {"data":[
              {"model":"gpt-9-luna","supportedReasoningEfforts":[{"reasoningEffort":"max"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-8-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"fast"}]},
              {"model":"gpt-7-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-99-sol","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}
            ]}
            """);

        Assert.Equal("gpt-7-luna", LunaModelSelector.Select([page])?.Model);
    }

    [Fact]
    public void Select_RequiresLowEvenWhenTheNewestModelOffersNone()
    {
        var page = Page("""
            {"data":[
              {"model":"gpt-6-luna","supportedReasoningEfforts":[{"reasoningEffort":"none"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}
            ]}
            """);

        Assert.Equal(new LunaModelSelection("gpt-5.6-luna", "low", "priority"), LunaModelSelector.Select([page]));
    }

    [Fact]
    public void Select_ReturnsNullWhenThereIsNoValidSupportedLunaCandidate()
    {
        var page = Page("""
            {"data":[
              {"model":"gpt-99-sol","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-x-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-6..2-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]},
              {"model":"gpt-8-luna"},
              {"model":"gpt-7-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[]},
              "malformed entry"
            ]}
            """);

        Assert.Null(LunaModelSelector.Select([page]));
    }

    [Fact]
    public void Select_ThrowsForMalformedPageEvenAfterFindingAnOlderCandidate()
    {
        var supported = Page("""
            {"data":[{"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}]}
            """);
        var malformed = Page("""{"data":"not an array"}""");

        Assert.Throws<JsonException>(() => LunaModelSelector.Select([supported, malformed]));
        Assert.Throws<JsonException>(() => LunaModelSelector.Select([Page("{}") ]));
    }

    [Fact]
    public void Select_KeepsTheFirstCandidateWhenNumericVersionsTie()
    {
        var first = Page("""
            {"data":[{"model":"gpt-6.02-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}]}
            """);
        var second = Page("""
            {"data":[{"model":"gpt-6.2-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}]}
            """);

        Assert.Equal("gpt-6.02-luna", LunaModelSelector.Select([first, second])?.Model);
        Assert.Equal("gpt-6.2-luna", LunaModelSelector.Select([second, first])?.Model);
    }

    [Fact]
    public void Select_ReturnsAnImmutableCoreSelectionValue()
    {
        var page = Page("""
            {"data":[{"model":"gpt-5.6-luna","supportedReasoningEfforts":[{"reasoningEffort":"low"}],"serviceTiers":[{"id":"priority"}]}]}
            """);

        var selection = LunaModelSelector.Select([page]);
        var changedCopy = selection! with { Effort = "medium" };

        Assert.Equal(new LunaModelSelection("gpt-5.6-luna", "low", "priority"), selection);
        Assert.Equal("medium", changedCopy.Effort);
        Assert.Equal("low", selection.Effort);
    }

    private static JsonElement Page(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
