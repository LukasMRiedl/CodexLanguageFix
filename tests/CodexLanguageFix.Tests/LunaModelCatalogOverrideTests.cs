using System.Text.Json;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class LunaModelCatalogOverrideTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clf-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void TryCreate_AddsNoneOnlyToPrivateCopy()
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
            ["none", "max"],
            efforts.EnumerateArray().Select(item => item.GetProperty("effort").GetString()));
        Assert.Equal("max", document.RootElement.GetProperty("models")[0].GetProperty("default_reasoning_level").GetString());
        Assert.Equal("list", document.RootElement.GetProperty("models")[0].GetProperty("visibility").GetString());
    }

    [Fact]
    public void TryCreate_DoesNotDuplicateExistingNone()
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
