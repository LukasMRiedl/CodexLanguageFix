using CodexLanguageFix.Core;
using System.Text.Json;

namespace CodexLanguageFix.Contracts;

public interface ICodexAppServerClient : IDisposable
{
    Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken);

    Task ConnectChatGptAsync(CancellationToken cancellationToken);

    Task<LunaModelSelection> ResolveLunaProfileAsync(CancellationToken cancellationToken);

    Task<string> RunCorrectionAsync(string protectedText, LunaModelSelection selection, CancellationToken cancellationToken);
}

internal interface IStructuredCodexCorrectionClient
{
    Task<string> RunStructuredCorrectionAsync(
        string inputJson,
        JsonElement outputSchema,
        string developerInstructions,
        LunaModelSelection selection,
        CancellationToken cancellationToken);
}
