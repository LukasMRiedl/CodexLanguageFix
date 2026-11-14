using System.Globalization;
using System.Text;
using System.Text.Json;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

Console.InputEncoding = new UTF8Encoding(false, true);
Console.OutputEncoding = new UTF8Encoding(false, true);
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string? line;
while ((line = Console.ReadLine()) != null)
{
    try
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.TryGetProperty("metadata", out _))
        {
            var prompt = LunaCorrectionProvider.DeveloperPrompt;
            Console.WriteLine(JsonSerializer.Serialize(new {
                developerPromptSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(prompt))),
                utf16Length = prompt.Length,
                crlfCount = prompt.Split("\r\n").Length - 1,
                lfCount = prompt.Count(c => c == '\n')
            }, options));
            continue;
        }
        if (root.TryGetProperty("settingsJson", out var settingsJsonValue))
        {
            var directory = Path.Combine(Path.GetTempPath(), "CodexLanguageFix.NativeSettingsOracle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var fixture = settingsJsonValue.GetString() ?? string.Empty;
                File.WriteAllText(Path.Combine(directory, "settings.json"), fixture, new UTF8Encoding(false, true));
                var settings = new SettingsService(directory).Load();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    settingsVersion = settings.SettingsVersion,
                    enabled = settings.Enabled,
                    startWithWindows = settings.StartWithWindows,
                    prepareLunaCorrections = settings.PrepareLunaCorrections,
                    firstRunNoticeShown = settings.FirstRunNoticeShown,
                    language = settings.Language ?? AppLocalizer.Automatic,
                    shortcutModifiers = settings.ShortcutModifiers,
                    shortcutVirtualKey = settings.ShortcutVirtualKey,
                    shortcutResetOnLoad = settings.ShortcutResetOnLoad
                }, options));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            continue;
        }
        var original = root.GetProperty("original").GetString()!;
        var corrected = root.GetProperty("corrected").GetString()!;
        if (root.TryGetProperty("coreCostProbe", out _))
        {
            Console.WriteLine(JsonSerializer.Serialize(CoreCostProbe.Run(original, corrected), options));
            continue;
        }
        var spans = ProtectedSpanDetector.Detect(original);
        var protectedText = LunaProtectedText.Create(original);
        var self = protectedText.TryRestore(protectedText.Text, out var selfRestored);
        var damaged = protectedText.TryRestore(protectedText.Text + "\u27e6CLF_PROTECTED_FF_0001\u27e7", out _);
        var plan = ComposerEditPlan.Create(original, corrected);
        var restored = TextStructure.TryRestoreLayout(original, corrected, out var layout);
        Console.WriteLine(JsonSerializer.Serialize(new {
            spans = spans.Select(s => new { s.Start, s.Length }),
            selfRestore = self && selfRestored == original,
            damagedRestore = damaged,
            structure = TextStructure.IsPreserved(original, corrected),
            restored = restored ? layout : null,
            plan = plan?.Edits,
            reverse = plan?.Reverse().Edits,
            repeated = !RepeatedSentenceGuard.HasReducedUnchangedRepetitions(original, corrected),
            graphemes = StringInfo.ParseCombiningCharacters(original),
            cacheKey = LunaCorrectionCache.CreateKey(original, "model\0low\0priority\0test")
        }, options));
    }
    catch (Exception e) { Console.WriteLine(JsonSerializer.Serialize(new { error = e.GetType().Name })); }
}
