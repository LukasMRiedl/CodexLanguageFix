using CodexLanguageFix.Core;
using System.Text.Json;

namespace CodexLanguageFix.Contracts;

public interface ICodexAppServerClient : IDisposable
{
    Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken);

    Task ConnectChatGptAsync(CancellationToken cancellationToken);

    Task<bool> SupportsLunaAsync(CancellationToken cancellationToken);

    Task<string> RunCorrectionAsync(string protectedText, CancellationToken cancellationToken);
}

internal interface IStructuredCodexCorrectionClient
{
    Task<string> RunStructuredCorrectionAsync(
        string inputJson,
        JsonElement outputSchema,
        string developerInstructions,
        string effort,
        CancellationToken cancellationToken);
}
