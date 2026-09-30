using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Infrastructure;

internal static class LunaModelSelector
{
    private const string ReasoningEffort = "low";
    private const string ServiceTier = "priority";

    internal static LunaModelSelection? Select(IEnumerable<JsonElement> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        LunaModelSelection? best = null;
        List<BigInteger>? bestVersion = null;

        foreach (var page in pages)
        {
            if (page.ValueKind != JsonValueKind.Object
                || !page.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Each model/list page must contain an array-valued 'data' property.");
            }

            foreach (var model in data.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object
                    || !model.TryGetProperty("model", out var modelElement)
                    || modelElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var modelName = modelElement.GetString();
                if (modelName is null
                    || !TryParseVersion(modelName, out var version)
                    || !Supports(model, "supportedReasoningEfforts", "reasoningEffort", ReasoningEffort)
                    || !Supports(model, "serviceTiers", "id", ServiceTier))
                {
                    continue;
                }

                // Gleichwertige numerische Versionen behalten den ersten Treffer der
                // Pagination. Damit hängt das Ergebnis weder von String-Sortierung
                // noch von einem zusätzlichen Sortier-Framework ab.
                if (bestVersion is not null && CompareVersions(version, bestVersion) <= 0)
                {
                    continue;
                }

                best = new LunaModelSelection(modelName, ReasoningEffort, ServiceTier);
                bestVersion = version;
            }
        }

        return best;
    }

    private static bool Supports(JsonElement model, string collectionName, string propertyName, string expectedValue)
    {
        if (!model.TryGetProperty(collectionName, out var collection)
            || collection.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var option in collection.EnumerateArray())
        {
            if (option.ValueKind == JsonValueKind.Object
                && option.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), expectedValue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseVersion(string modelName, out List<BigInteger> version)
    {
        version = [];
        const string prefix = "gpt-";
        const string suffix = "-luna";

        if (!modelName.StartsWith(prefix, StringComparison.Ordinal)
            || !modelName.EndsWith(suffix, StringComparison.Ordinal)
            || modelName.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var versionText = modelName.AsSpan(prefix.Length, modelName.Length - prefix.Length - suffix.Length);
        if (versionText.IsEmpty)
        {
            return false;
        }

        var componentStart = 0;
        for (var index = 0; index <= versionText.Length; index++)
        {
            if (index < versionText.Length && versionText[index] != '.')
            {
                if (!char.IsAsciiDigit(versionText[index]))
                {
                    return false;
                }

                continue;
            }

            if (componentStart == index
                || !BigInteger.TryParse(
                    versionText[componentStart..index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var component))
            {
                return false;
            }

            version.Add(component);
            componentStart = index + 1;
        }

        return version.Count > 0;
    }

    private static int CompareVersions(IReadOnlyList<BigInteger> left, IReadOnlyList<BigInteger> right)
    {
        var count = Math.Max(left.Count, right.Count);
        for (var index = 0; index < count; index++)
        {
            var leftComponent = index < left.Count ? left[index] : BigInteger.Zero;
            var rightComponent = index < right.Count ? right[index] : BigInteger.Zero;
            var comparison = leftComponent.CompareTo(rightComponent);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
