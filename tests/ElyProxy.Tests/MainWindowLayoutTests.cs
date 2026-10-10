using System.Xml.Linq;

namespace ElyProxy.Tests;

/// <summary>
/// Layout regression tests. The sidebar status card previously kept its text
/// in a horizontal StackPanel, which measures children with infinite width, so
/// long status strings were clipped instead of wrapped. These tests lock the
/// width-constrained layout in MainWindow.xaml.
/// </summary>
public sealed class MainWindowLayoutTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void SidebarStatusText_WrapsAndIsWidthConstrained()
    {
        var document = XDocument.Load(Path.Combine(FindWorkspaceRoot(), "Views", "MainWindow.xaml"));

        var card = document.Descendants(Presentation + "Border")
            .FirstOrDefault(element => (string?)element.Attribute("Style") == "{StaticResource AppleCard}");
        Assert.NotNull(card);

        var status = card.Descendants(Presentation + "TextBlock")
            .FirstOrDefault(element => (string?)element.Attribute("Text") == "{Binding StatusText}");
        Assert.NotNull(status);

        Assert.Equal("Wrap", (string?)status.Attribute("TextWrapping"));

        var parent = status.Parent;
        Assert.NotNull(parent);
        Assert.Equal(Presentation + "Grid", parent.Name);
        Assert.Contains(
            parent.Elements(Presentation + "Grid.ColumnDefinitions").Descendants(Presentation + "ColumnDefinition"),
            column => (string?)column.Attribute("Width") == "*");
    }

    [Fact]
    public void TruncatedShellTexts_ProvideFullTextTooltips()
    {
        var document = XDocument.Load(Path.Combine(FindWorkspaceRoot(), "Views", "MainWindow.xaml"));

        var trimmed = document.Descendants(Presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("TextTrimming") == "CharacterEllipsis")
            .Where(element => ((string?)element.Attribute("Text"))?.StartsWith("{Binding", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(trimmed);
        Assert.All(trimmed, element =>
        {
            var toolTip = (string?)element.Attribute("ToolTip");
            Assert.False(string.IsNullOrWhiteSpace(toolTip), $"missing tooltip for '{element.Attribute("Text")?.Value}'");
        });
    }

    [Fact]
    public void TorrentSwitch_IsAvailableOnlyInSettings()
    {
        var shell = XDocument.Load(Path.Combine(FindWorkspaceRoot(), "Views", "MainWindow.xaml"));
        var settings = XDocument.Load(Path.Combine(FindWorkspaceRoot(), "Views", "Sections", "SettingsView.xaml"));
        Assert.DoesNotContain(shell.Descendants().Attributes(), attribute => attribute.Value.Contains("{Binding BlockTorrent"));
        Assert.Single(settings.Descendants().Attributes().Where(attribute => attribute.Value.Contains("{Binding BlockTorrent")));
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ElyProxy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("ElyProxy.sln not found above " + AppContext.BaseDirectory);
    }
}
