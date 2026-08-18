namespace CodexLanguageFix.Core;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool FirstRunNoticeShown { get; set; }
    public string Language { get; set; } = AppLocalizer.Automatic;
    public CorrectionProviderKind CorrectionProvider { get; set; } = CorrectionProviderKind.LanguageTool;
}
