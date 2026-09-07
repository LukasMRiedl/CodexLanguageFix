using System.Windows;
using System.Windows.Controls;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.UI;

internal sealed class RecoveryWindow : Window
{
    public RecoveryWindow(string original, AppLocalizer localizer)
    {
        Title = localizer.Get(AppText.RecoveryOriginal);
        Width = 640;
        Height = 440;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var explanation = new TextBlock
        {
            Text = localizer.Get(AppText.RecoveryExplanation),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(explanation, Dock.Top);
        panel.Children.Add(explanation);
        panel.Children.Add(new System.Windows.Controls.TextBox
        {
            Text = original, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        Content = panel;
    }
}
