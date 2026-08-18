using CodexLanguageFix.Core;

namespace CodexLanguageFix.Contracts;

public interface ICorrectionProvider
{
    CorrectionProviderKind Kind { get; }

    Task<CorrectionProviderResult> CorrectAsync(string text, CancellationToken cancellationToken);

    Task<CorrectionProviderHealth> TestAsync(CancellationToken cancellationToken);
}

public interface IOpenAiConnection
{
    Task ConnectAsync(CancellationToken cancellationToken);
}
