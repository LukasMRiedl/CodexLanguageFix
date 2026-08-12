using System.Windows;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.UI;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow(AppLocalizer localizer)
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(localizer.Culture.IetfLanguageTag);
        HeadingText.Text = localizer.Get(AppText.FirstRunHeading);
        ExplanationText.Text = localizer.Get(AppText.FirstRunExplanation);
        PrivacyText.Text = localizer.Get(AppText.FirstRunPrivacy);
        AcceptButton.Content = localizer.Get(AppText.Acknowledge);
    }

    private void Accept_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
