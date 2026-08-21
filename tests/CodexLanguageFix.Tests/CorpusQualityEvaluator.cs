using System.Text.Json;

namespace CodexLanguageFix.Tests;

internal sealed record CorpusCase(
    string Id,
    string Language,
    string Kind,
    string Category,
    IReadOnlyList<string> Tags,
    string Input,
    string Target,
    IReadOnlyList<string> AcceptableOutputs,
    IReadOnlyList<string> MustPreserve,
    IReadOnlyList<string> MustContain,
    IReadOnlyList<string> MustNotContain,
    int? MaxEditDistance,
    double Weight)
{
    public bool IsControl => string.Equals(Kind, "control", StringComparison.Ordinal);

    public bool HasAlternatives => AcceptableOutputs.Count > 1;
}

internal sealed record CorpusEvaluation(
    CorpusCase Case,
    string? Actual,
    bool Accepted,
    bool ControlUnchanged,
    bool MustPreservePassed,
    bool MustContainPassed,
    bool MustNotContainPassed,
    bool MinimalEditPassed,
    bool NoForbiddenControlCharacters)
{
    public bool HardGatePassed =>
        Actual is not null
        && Accepted
        && ControlUnchanged
        && MustPreservePassed
        && MustContainPassed
        && MustNotContainPassed
        && MinimalEditPassed
        && NoForbiddenControlCharacters;

    public bool ActualChanged =>
        Actual is not null
        && !string.Equals(Actual, Case.Input, StringComparison.Ordinal);

    public bool IsTruePositive => !Case.IsControl && HardGatePassed;

    public bool IsFalseNegative => !Case.IsControl && !HardGatePassed;

    // Eine falsche Änderung ist zugleich eine falsche positive Änderung und
    // eine verfehlte Korrektur. Unveränderte Korrekturfälle bleiben nur FN.
    public bool IsFalsePositive =>
        ActualChanged && (Case.IsControl || !HardGatePassed);
}

internal sealed record CorpusScore(
    IReadOnlyList<CorpusEvaluation> Evaluations,
    int TruePositives,
    int FalsePositives,
    int FalseNegatives,
    int Controls,
    int ControlsPassed,
    double WeightedTruePositives,
    double WeightedFalsePositives,
    double WeightedFalseNegatives,
    double WeightedControls,
    double WeightedControlsPassed)
{
    public double F05 => QualityStatistics.FScore(
        0.5,
        WeightedTruePositives,
        WeightedFalsePositives,
        WeightedFalseNegatives);

    public bool HardGatesPassed =>
        Evaluations.Count > 0 && Evaluations.All(item => item.HardGatePassed);

    public double ControlPassRate =>
        WeightedControls == 0 ? 1 : WeightedControlsPassed / WeightedControls;
}

internal static class QualityCorpus
{
    public const int SchemaVersion = 2;

    public static readonly string[] CorrectionLanguages = ["de", "en"];

    public static readonly string[] RetentionLanguages = ["es", "fr"];

    public static readonly string[] Languages = [.. CorrectionLanguages, .. RetentionLanguages];

    public static IReadOnlyList<CorpusCase> Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "benchmark_cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || schemaVersion.GetInt32() != SchemaVersion)
        {
            throw new InvalidDataException($"Das Korpus benötigt schemaVersion={SchemaVersion}.");
        }

        var cases = new List<CorpusCase>();
        var languageArrays = document.RootElement
            .EnumerateObject()
            .Where(property => property.Name is not "schemaVersion"
                               && property.Value.ValueKind == JsonValueKind.Array)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        if (languageArrays.Length == 0)
        {
            throw new InvalidDataException("Das Korpus enthält keine Sprachfall-Arrays.");
        }

        foreach (var languageProperty in languageArrays)
        {
            var rootLanguage = languageProperty.Name;

            foreach (var item in languageProperty.Value.EnumerateArray())
            {
                var id = RequiredString(item, "id");
                var itemLanguage = RequiredString(item, "language");
                var kind = RequiredString(item, "kind");
                if (!string.Equals(itemLanguage, rootLanguage, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Korpusfall '{id}' liegt unter der falschen Sprache '{rootLanguage}'.");
                }

                if (kind is not ("correction" or "control"))
                {
                    throw new InvalidDataException(
                        $"Korpusfall '{id}' hat einen unbekannten Typ '{kind}'.");
                }

                var category = RequiredString(item, "category");
                var input = RequiredString(item, "input");
                var target = RequiredString(item, "target");
                var acceptableOutputs = ReadStrings(item, "acceptedOutputs");
                if (acceptableOutputs.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Korpusfall '{id}' benötigt mindestens ein akzeptiertes Ausgabeziel.");
                }

                if (kind == "control"
                    && (!string.Equals(input, target, StringComparison.Ordinal)
                        || !acceptableOutputs.Contains(input, StringComparer.Ordinal)))
                {
                    throw new InvalidDataException(
                        $"Kontrollfall '{id}' muss unverändert und akzeptiert sein.");
                }

                if (kind == "correction"
                    && (!CorrectionLanguages.Contains(rootLanguage, StringComparer.Ordinal)
                        || string.Equals(input, target, StringComparison.Ordinal)
                        || acceptableOutputs.Contains(input, StringComparer.Ordinal)))
                {
                    throw new InvalidDataException(
                        $"Korrekturfall '{id}' ist für die Korrektursprachen oder Orakel ungültig.");
                }

                if (!Languages.Contains(rootLanguage, StringComparer.Ordinal)
                    && (kind != "control"
                        || !string.Equals(input, target, StringComparison.Ordinal)
                        || !acceptableOutputs.Contains(input, StringComparer.Ordinal)))
                {
                    throw new InvalidDataException(
                        $"Nicht unterstützte Sprache '{rootLanguage}' darf nur als unveränderte Erhaltungskontrolle vorkommen ('{id}').");
                }

                var weight = RequiredWeight(item, id);
                cases.Add(new CorpusCase(
                    id,
                    itemLanguage,
                    kind,
                    category,
                    ReadStrings(item, "tags"),
                    input,
                    target,
                    acceptableOutputs,
                    ReadStrings(item, "mustPreserve"),
                    ReadStrings(item, "mustContain"),
                    ReadStrings(item, "mustNotContain"),
                    item.TryGetProperty("maxEditDistance", out var maxEditDistance)
                        ? maxEditDistance.GetInt32()
                        : null,
                    weight));
            }
        }

        return cases;
    }

    private static string RequiredString(JsonElement item, string property)
    {
        if (item.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(value.GetString()))
        {
            return value.GetString()!;
        }

        throw new InvalidDataException($"Korpusfall ohne gültiges Feld '{property}'.");
    }

    private static double RequiredWeight(JsonElement item, string id)
    {
        if (!item.TryGetProperty("weight", out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var weight)
            || double.IsNaN(weight)
            || double.IsInfinity(weight)
            || weight <= 0)
        {
            throw new InvalidDataException(
                $"Korpusfall '{id}' benötigt ein positives, endliches Gewicht.");
        }

        return weight;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Das Korpusfeld '{property}' muss ein Array sein.");
        }

        return value.EnumerateArray()
            .Select(element => element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null)
            .Where(value => !string.IsNullOrEmpty(value))
            .Cast<string>()
            .ToArray();
    }
}

internal static class CorpusQualityEvaluator
{
    public static CorpusEvaluation Evaluate(CorpusCase corpusCase, string? actual)
    {
        ArgumentNullException.ThrowIfNull(corpusCase);

        var accepted = actual is not null
            && corpusCase.AcceptableOutputs.Any(expected =>
                string.Equals(expected, actual, StringComparison.Ordinal));
        var controlUnchanged = !corpusCase.IsControl
            || string.Equals(corpusCase.Input, actual, StringComparison.Ordinal);
        var mustPreserve = actual is not null
            && PreservesProtectedText(corpusCase, actual);
        var mustContain = actual is not null
            && corpusCase.MustContain.All(value => actual.Contains(value, StringComparison.Ordinal));
        var mustNotContain = actual is not null
            && corpusCase.MustNotContain.All(value => !actual.Contains(value, StringComparison.Ordinal));
        var minimalEdit = actual is not null
            && (!corpusCase.MaxEditDistance.HasValue
                || LevenshteinDistance(corpusCase.Input, actual) <= corpusCase.MaxEditDistance.Value);
        var noForbiddenControlCharacters = actual is not null
            && actual.All(character => !char.IsControl(character)
                || character is '\r' or '\n' or '\t');

        return new CorpusEvaluation(
            corpusCase,
            actual,
            accepted,
            controlUnchanged,
            mustPreserve,
            mustContain,
            mustNotContain,
            minimalEdit,
            noForbiddenControlCharacters);
    }

    public static CorpusScore Score(IEnumerable<CorpusEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        var materialized = evaluations.ToArray();
        return new CorpusScore(
            materialized,
            materialized.Count(item => item.IsTruePositive),
            materialized.Count(item => item.IsFalsePositive),
            materialized.Count(item => item.IsFalseNegative),
            materialized.Count(item => item.Case.IsControl),
            materialized.Count(item => item.Case.IsControl && item.HardGatePassed),
            materialized.Where(item => item.IsTruePositive).Sum(item => item.Case.Weight),
            materialized.Where(item => item.IsFalsePositive).Sum(item => item.Case.Weight),
            materialized.Where(item => item.IsFalseNegative).Sum(item => item.Case.Weight),
            materialized.Where(item => item.Case.IsControl).Sum(item => item.Case.Weight),
            materialized.Where(item => item.Case.IsControl && item.HardGatePassed)
                .Sum(item => item.Case.Weight));
    }

    internal static bool PreservesProtectedText(CorpusCase corpusCase, string actual)
    {
        var inputOffset = 0;
        var actualOffset = 0;
        foreach (var value in corpusCase.MustPreserve)
        {
            var inputIndex = corpusCase.Input.IndexOf(value, inputOffset, StringComparison.Ordinal);
            var actualIndex = actual.IndexOf(value, actualOffset, StringComparison.Ordinal);
            if (inputIndex < 0
                || actualIndex < 0
                || CountOccurrences(corpusCase.Input, value) != CountOccurrences(actual, value))
            {
                return false;
            }

            inputOffset = inputIndex + value.Length;
            actualOffset = actualIndex + value.Length;
        }

        return true;
    }

    internal static int CountOccurrences(string text, string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        var count = 0;
        for (var offset = 0;
             (offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0;
             offset += value.Length)
        {
            count++;
        }

        return count;
    }

    internal static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[^1];
    }
}
