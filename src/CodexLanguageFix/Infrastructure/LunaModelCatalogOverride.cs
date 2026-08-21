using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexLanguageFix.Infrastructure;

internal static partial class LunaModelCatalogOverride
{
    private const string OutputFileName = "luna-model-catalog.json";

    public static string? TryCreate(string runtimeDirectory, string? codexHome = null)
    {
        try
        {
            codexHome ??= ResolveCodexHome();
            var configPath = Path.Combine(codexHome, "config.toml");
            if (!File.Exists(configPath))
            {
                return null;
            }

            var sourcePath = ReadCatalogPath(File.ReadAllText(configPath), codexHome);
            if (sourcePath is null || !File.Exists(sourcePath))
            {
                return null;
            }

            var root = JsonNode.Parse(File.ReadAllText(sourcePath)) as JsonObject;
            var models = root?["models"] as JsonArray;
            var luna = models?
                .OfType<JsonObject>()
                .FirstOrDefault(model => string.Equals(
                    model["slug"]?.GetValue<string>(),
                    CodexAppServerClient.LunaModel,
                    StringComparison.Ordinal));
            var efforts = luna?["supported_reasoning_levels"] as JsonArray;
            if (root is null || luna is null || efforts is null)
            {
                return null;
            }

            var configuredEfforts = efforts
                .OfType<JsonObject>()
                .Select(option => option["effort"]?.GetValue<string>())
                .Where(effort => !string.IsNullOrWhiteSpace(effort))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var (effort, description) in SupportedEfforts.Reverse())
            {
                if (configuredEfforts.Contains(effort))
                {
                    continue;
                }

                efforts.Insert(0, new JsonObject
                {
                    ["effort"] = effort,
                    ["description"] = description
                });
            }

            Directory.CreateDirectory(runtimeDirectory);
            var outputPath = Path.Combine(runtimeDirectory, OutputFileName);
            var temporaryPath = outputPath + ".tmp";
            File.WriteAllText(temporaryPath, root.ToJsonString(SerializerOptions));
            File.Move(temporaryPath, outputPath, true);
            return outputPath;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    internal static string? ReadCatalogPath(string config, string codexHome)
    {
        var match = CatalogPathRegex().Match(config);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["single"].Success
            ? match.Groups["single"].Value
            : JsonSerializer.Deserialize<string>($"\"{match.Groups["double"].Value}\"");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(codexHome, value));
    }

    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : Path.GetFullPath(configured);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private static readonly (string Effort, string Description)[] SupportedEfforts =
    [
        ("none", "No reasoning for lowest-latency text correction"),
        ("low", "Low reasoning for text correction"),
        ("medium", "Medium reasoning for text correction")
    ];

    [GeneratedRegex(
        "(?m)^\\s*model_catalog_json\\s*=\\s*(?:'(?<single>[^']+)'|\\\"(?<double>(?:\\\\.|[^\\\"])*)\\\")\\s*(?:#.*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CatalogPathRegex();
}
