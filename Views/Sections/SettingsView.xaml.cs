using System.Windows;
using System.Windows.Controls;

namespace ElyProxy.Views.Sections;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void JumpToSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && FindName(name) is FrameworkElement section)
            section.BringIntoView();
    }
}
