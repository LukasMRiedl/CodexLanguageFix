using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using System.Diagnostics;

namespace CodexLanguageFix.Infrastructure;

public sealed class LanguageToolCorrectionProvider(
    ILanguageToolClient client,
    ICorrectionEngine engine) : ICorrectionProvider
{
    public CorrectionProviderKind Kind => CorrectionProviderKind.LanguageTool;

    public async Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var annotated = engine.Annotate(text);
        var check = await client.CheckAsync(annotated, cancellationToken).ConfigureAwait(false);
        var outcome = engine.Apply(text, annotated, check.Matches);
        stopwatch.Stop();
        return new CorrectionProviderResult(
            outcome.CorrectedText,
            outcome.Corrections.Count,
            Kind,
            check.StatusCode,
            stopwatch.Elapsed);
    }

    public async Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken)
    {
        var result = await CorrectAsync("This is a connection test.", cancellationToken).ConfigureAwait(false);
        return new CorrectionProviderHealth(Kind, result.Elapsed);
    }
}
