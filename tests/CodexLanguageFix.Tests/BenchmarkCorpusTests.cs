using System.Text.Json;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class BenchmarkCorpusTests
{
    [Fact]
    public void ExistingGermanAndEnglishCorpus_IsHandledWithoutOffsetCorruption()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "benchmark_cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var engine = new CorrectionEngine();
        var total = 0;
        var controls = 0;

        foreach (var language in new[] { "de", "en" })
        {
            foreach (var item in document.RootElement.GetProperty(language).EnumerateArray())
            {
                total++;
                var input = item.GetProperty("input").GetString()!;
                var target = item.GetProperty("target").GetString()!;
                var prompt = engine.Annotate(input);
                IReadOnlyList<LanguageToolMatch> matches;
                if (input == target)
                {
                    controls++;
                    matches = [];
                }
                else
                {
                    var (offset, inputLength, replacement) = SingleReplacement(input, target);
                    matches = [new LanguageToolMatch(offset, inputLength, [replacement], "CORPUS", "TEST", "test", 1, 0)];
                }

                var outcome = engine.Apply(input, prompt, matches);
                Assert.Equal(target, outcome.CorrectedText);
            }
        }

        Assert.Equal(38, total);
        Assert.Equal(7, controls);
    }

    private static (int Offset, int InputLength, string Replacement) SingleReplacement(string input, string target)
    {
        var prefix = 0;
        while (prefix < input.Length && prefix < target.Length && input[prefix] == target[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < input.Length - prefix
               && suffix < target.Length - prefix
               && input[input.Length - suffix - 1] == target[target.Length - suffix - 1])
        {
            suffix++;
        }

        return (
            prefix,
            input.Length - prefix - suffix,
            target.Substring(prefix, target.Length - prefix - suffix));
    }
}
