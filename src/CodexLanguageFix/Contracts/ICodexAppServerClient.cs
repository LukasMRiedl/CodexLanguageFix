using CodexLanguageFix.Core;

namespace CodexLanguageFix.Contracts;

public interface ICodexAppServerClient : IDisposable
{
    Task<CodexAccountState> GetAccountAsync(CancellationToken cancellationToken);

    Task ConnectChatGptAsync(CancellationToken cancellationToken);

    Task<bool> SupportsLunaLowAsync(CancellationToken cancellationToken);

    Task<string> RunCorrectionAsync(string protectedText, CancellationToken cancellationToken);
}
