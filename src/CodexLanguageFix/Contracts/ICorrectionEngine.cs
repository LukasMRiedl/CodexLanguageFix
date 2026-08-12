using CodexLanguageFix.Core;

namespace CodexLanguageFix.Contracts;

public interface ICorrectionEngine
{
    AnnotatedPrompt Annotate(string text);
    CorrectionOutcome Apply(string original, AnnotatedPrompt prompt, IReadOnlyList<LanguageToolMatch> matches);
}
