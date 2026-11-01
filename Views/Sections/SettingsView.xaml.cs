using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ElyProxy.Views.Sections;

public partial class SettingsView : UserControl
{
    private readonly Dictionary<Button, string> _tabs = new();
    private readonly List<SearchEntry> _entries = new();
    private sealed record SearchEntry(string Title, string Section, FrameworkElement Target);

    public SettingsView()
    {
        InitializeComponent();
        foreach (var button in SettingsNavigation.Children.OfType<Button>())
        {
            string section = (string)button.Tag;
            _tabs[button] = section;
            if (FindName(section.Replace("Anchor", "Panel")) is StackPanel panel)
                foreach (var text in Descendants(panel).OfType<TextBlock>())
                    if (!string.IsNullOrWhiteSpace(text.Text) && System.Windows.Data.BindingOperations.GetBinding(text, TextBlock.TextProperty) == null)
                        _entries.Add(new SearchEntry($"{button.Content} · {text.Text}", section, text));
        }
        NavigateToSection("AppearanceAnchor");
    }

    public void NavigateToSection(string section)
    {
        foreach (var tab in _tabs)
        {
            bool active = tab.Value == section;
            tab.Key.Tag = active ? "Selected" : tab.Value;
            if (FindName(tab.Value.Replace("Anchor", "Panel")) is FrameworkElement panel)
                panel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
        SettingsScroll.ScrollToTop();
        if (section == "CoresAnchor" && DataContext is ElyProxy.ViewModels.MainViewModel viewModel)
            _ = viewModel.RefreshSettingsCoreVersionsAsync();
    }

    private void JumpToSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && _tabs.TryGetValue(button, out var section)) NavigateToSection(section);
    }

    private void SettingsColumns_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid || grid.Children.Count < 2) return;
        bool wide = e.NewSize.Width >= 940;
        var left = (FrameworkElement)grid.Children[0];
        var right = (FrameworkElement)grid.Children[1];
        double ratio = double.TryParse(left.Tag?.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
        grid.ColumnDefinitions[0].Width = new GridLength(wide ? ratio : 1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(right, wide ? 1 : 0);
        Grid.SetRow(right, wide ? 0 : 1);
        left.Margin = wide ? new Thickness(0, 0, 16, 0) : new Thickness(0);
    }

    private void PreviewConnect_Click(object sender, RoutedEventArgs e) =>
        ElyProxy.Services.DialogService.Current.Notify("Предпросмотр", "Так выглядит кнопка подключения в выбранной теме.");

    private void OpenSubscriptions_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.FindName("NavList") is ListBox navigation)
            navigation.SelectedIndex = 1;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchResults == null) return;
        string query = SettingsSearch.Text.Trim();
        SearchResults.ItemsSource = query.Length == 0 ? null : _entries.Where(p => p.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(25).ToArray();
        SearchResults.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is not SearchEntry entry) return;
        NavigateToSection(entry.Section);
        SettingsSearch.Clear();
        DependencyObject? parent = entry.Target;
        while (parent != null)
        {
            if (parent is Expander expander) expander.IsExpanded = true;
            parent = LogicalTreeHelper.GetParent(parent);
        }
        Dispatcher.BeginInvoke(new Action(() => entry.Target.BringIntoView()));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
