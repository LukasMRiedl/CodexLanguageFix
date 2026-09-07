using System.Text.Json;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaModelCatalogOverrideTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clf-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void TryCreate_AddsSupportedEffortsOnlyToPrivateCopy()
    {
        var codexHome = Path.Combine(_root, ".codex");
        var runtime = Path.Combine(_root, "runtime");
        var source = Path.Combine(codexHome, "catalog.json");
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(source, CatalogJson);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), "model_catalog_json = 'catalog.json'\n");

        var output = LunaModelCatalogOverride.TryCreate(runtime, codexHome);

        Assert.NotNull(output);
        Assert.NotEqual(source, output);
        Assert.DoesNotContain("\"effort\":\"none\"", File.ReadAllText(source), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(output!));
        var efforts = document.RootElement.GetProperty("models")[0].GetProperty("supported_reasoning_levels");
        Assert.Equal(
            ["none", "low", "medium", "max"],
            efforts.EnumerateArray().Select(item => item.GetProperty("effort").GetString()));
        Assert.Equal("max", document.RootElement.GetProperty("models")[0].GetProperty("default_reasoning_level").GetString());
        Assert.Equal("list", document.RootElement.GetProperty("models")[0].GetProperty("visibility").GetString());
    }

    [Fact]
    public void TryCreate_DoesNotDuplicateExistingSupportedEfforts()
    {
        var codexHome = Path.Combine(_root, ".codex");
        var runtime = Path.Combine(_root, "runtime");
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "catalog.json"), CatalogJson.Replace(
            "{\"effort\":\"max\",\"description\":\"Maximum\"}",
            "{\"effort\":\"none\",\"description\":\"No reasoning\"},{\"effort\":\"max\",\"description\":\"Maximum\"}",
            StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), "model_catalog_json = \"catalog.json\"\n");

        var output = LunaModelCatalogOverride.TryCreate(runtime, codexHome);

        using var document = JsonDocument.Parse(File.ReadAllText(output!));
        var efforts = document.RootElement.GetProperty("models")[0].GetProperty("supported_reasoning_levels");
        Assert.Single(efforts.EnumerateArray(), item => item.GetProperty("effort").GetString() == "none");
        Assert.Single(efforts.EnumerateArray(), item => item.GetProperty("effort").GetString() == "low");
        Assert.Single(efforts.EnumerateArray(), item => item.GetProperty("effort").GetString() == "medium");
        Assert.Single(efforts.EnumerateArray(), item => item.GetProperty("effort").GetString() == "max");
    }

    [Fact]
    public void TryCreate_ReturnsNullWithoutConfiguredCatalog()
    {
        var codexHome = Path.Combine(_root, ".codex");
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), "model = 'gpt-5.6-sol'\n");

        Assert.Null(LunaModelCatalogOverride.TryCreate(Path.Combine(_root, "runtime"), codexHome));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TryCreate_ReusesOnlyExistingPrivateCatalogByteExactly(bool configExists)
    {
        var codexHome = Path.Combine(_root, ".codex");
        var runtime = Path.Combine(_root, "runtime");
        Directory.CreateDirectory(codexHome);
        Directory.CreateDirectory(runtime);
        var source = Path.Combine(runtime, "luna-model-catalog.json");
        File.WriteAllText(source, PrivateCatalogJson);
        var bytes = File.ReadAllBytes(source);
        var modified = File.GetLastWriteTimeUtc(source);
        var config = Path.Combine(codexHome, "config.toml");
        if (configExists) File.WriteAllText(config, "model = 'gpt-5.6-luna'\n");

        var output = LunaModelCatalogOverride.TryCreate(runtime, codexHome);

        Assert.Equal(source, output);
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(source));
        Assert.Equal(configExists, File.Exists(config));
        Assert.False(File.Exists(source + ".tmp"));
    }

    [Theory]
    [InlineData("model_catalog_json = 'missing.json'\n")]
    [InlineData("model_catalog_json = ''\n")]
    [InlineData("model_catalog_json = invalid\n")]
    public void TryCreate_DoesNotHideInvalidExplicitCatalogWithPrivateCopy(string configText)
    {
        var codexHome = Path.Combine(_root, ".codex");
        var runtime = Path.Combine(_root, "runtime");
        Directory.CreateDirectory(codexHome);
        Directory.CreateDirectory(runtime);
        var source = Path.Combine(runtime, "luna-model-catalog.json");
        File.WriteAllText(source, PrivateCatalogJson);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), configText);
        var bytes = File.ReadAllBytes(source);

        Assert.Null(LunaModelCatalogOverride.TryCreate(runtime, codexHome));
        Assert.Equal(bytes, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"models\":[]}")]
    [InlineData("{\"models\":[{\"slug\":\"gpt-5.6-luna\"}]}")]
    [InlineData("{\"models\":[{\"slug\":\"gpt-5.6-luna\",\"base_instructions\":\"\",\"supported_reasoning_levels\":[{\"effort\":\"low\"}]}]}")]
    public void TryCreate_RejectsInvalidPrivateCopyWithoutChangingIt(string content)
    {
        var runtime = Path.Combine(_root, "runtime");
        Directory.CreateDirectory(runtime);
        var source = Path.Combine(runtime, "luna-model-catalog.json");
        File.WriteAllText(source, content);
        var bytes = File.ReadAllBytes(source);

        Assert.Null(LunaModelCatalogOverride.TryCreate(runtime, Path.Combine(_root, ".codex")));
        Assert.Equal(bytes, File.ReadAllBytes(source));
    }

    [Fact]
    public void TryCreate_DoesNotImportGlobalModelCacheForNewInstallation()
    {
        var codexHome = Path.Combine(_root, ".codex");
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "models_cache.json"), PrivateCatalogJson);
        Assert.Null(LunaModelCatalogOverride.TryCreate(Path.Combine(_root, "runtime"), codexHome));
        Assert.False(Directory.Exists(Path.Combine(_root, "runtime")));
    }

    private const string PrivateCatalogJson = """
        {"models":[{"slug":"gpt-5.6-luna","base_instructions":"Correct text.",
        "supported_reasoning_levels":[{"effort":"none"},{"effort":"low"},{"effort":"medium"},{"effort":"max"}],
        "service_tiers":[{"id":"priority"}]}]}
        """;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private const string CatalogJson = """
        {
          "models": [
            {
              "slug": "gpt-5.6-luna",
              "display_name": "GPT-5.6-Luna",
              "default_reasoning_level": "max",
              "supported_reasoning_levels": [
                {"effort":"max","description":"Maximum"}
              ],
              "visibility": "list"
            }
          ]
        }
        """;
}
