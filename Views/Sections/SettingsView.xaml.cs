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
    }

    private void JumpToSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && _tabs.TryGetValue(button, out var section)) NavigateToSection(section);
    }

    private void DpiOverview_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= 940;
        DpiOverview.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(DpiTuningCard, wide ? 1 : 0);
        Grid.SetRow(DpiTuningCard, wide ? 0 : 1);
        DpiProfileCard.Margin = wide ? new Thickness(0, 0, 16, 0) : new Thickness(0, 0, 0, 12);
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
