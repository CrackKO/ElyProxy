using System.Windows;
using System.Windows.Controls;

namespace ElyProxy.Controls;

/// <summary>
/// Small "i" badge that shows a detailed explanation on hover. The tooltip
/// reuses the app's themed ToolTip style (Menus.xaml).
/// </summary>
public class InfoHint : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(InfoHint),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var hint = (InfoHint)d;
        var text = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            ToolTipService.SetToolTip(hint, null);
            return;
        }

        var body = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
        ToolTipService.SetToolTip(hint, body);
    }
}
