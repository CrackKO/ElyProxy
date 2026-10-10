using System.Windows;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class TrayMenuPlacementTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1040, 1880, 1020, 210, 370)]
    [InlineData(0, 0, 1920, 1040, 20, 1020, 210, 370)]
    [InlineData(0, 0, 1920, 1040, 1880, 5, 210, 370)]
    [InlineData(-1920, 0, 1920, 1040, -1800, 1020, 210, 370)]
    [InlineData(1920, -200, 2560, 1400, 4400, 1180, 420, 740)]
    [InlineData(0, -1080, 1920, 1040, 1880, -50, 315, 555)]
    public void Menu_StaysInsideCursorMonitorIncludingScaledAndNegativeCoordinates(
        double x, double y, double width, double height, double cursorX, double cursorY, double menuWidth, double menuHeight)
    {
        var area = new Rect(x, y, width, height);
        var position = TrayMenuPlacement.Calculate(area, new Point(cursorX, cursorY), new Size(menuWidth, menuHeight), 8);
        Assert.True(area.Contains(new Rect(position, new Size(menuWidth, menuHeight))));
    }

    [Fact]
    public void OversizedMenu_DoesNotThrowOrProduceInvalidClampBounds()
    {
        Assert.Equal(new Point(8, 8), TrayMenuPlacement.Calculate(new Rect(0, 0, 100, 100), new Point(90, 90), new Size(200, 400), 8));
    }
}
