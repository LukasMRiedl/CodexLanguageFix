using System.Windows;

namespace CodexLanguageFix.UI;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow() => InitializeComponent();

    private void Accept_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
