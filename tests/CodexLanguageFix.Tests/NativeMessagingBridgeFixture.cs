using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class NativeMessagingBridgeFixture
{
    // The opt-in fixture uses a distinct pipe and accepts only synthetic test phrases.
    [Fact]
    public async Task ServeSyntheticCorrectionsForRealExtension()
    {
        if (Environment.GetEnvironmentVariable("CODEX_LANGUAGE_FIX_BROWSER_FIXTURE") != "1") return;
        using var provider = new PrioritizedCorrectionProvider(new FixtureProvider());
        using var server = new BrowserBridgeServer(provider,
            () => Task.FromResult(new BrowserState(true, false, 3, 188)), _ => { }, () => true,
            key => key, BrowserProtocol.PipeName + ".Test");
        await Task.Delay(TimeSpan.FromSeconds(180));
        server.Dispose();
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private sealed class FixtureProvider : ICorrectionProvider
    {
        public CorrectionProviderKind Kind => CorrectionProviderKind.Luna;
        public Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var corrected = text switch
            {
                "Das ist ein Tset." => "Das ist ein Test.",
                "Das ist ein Tset.\nUnd ncoh eins." => "Das ist ein Test.\nUnd noch eins.",
                _ => throw new InvalidOperationException("Only synthetic fixture input is allowed.")
            };
            return Task.FromResult(new CorrectionProviderResult(corrected, 1, Kind, null, TimeSpan.Zero));
        }
        public Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
