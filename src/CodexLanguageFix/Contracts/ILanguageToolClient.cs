using CodexLanguageFix.Core;

namespace CodexLanguageFix.Contracts;

public interface ILanguageToolClient
{
    Task<LanguageToolCheckResult> CheckAsync(AnnotatedPrompt prompt, CancellationToken cancellationToken);
}
