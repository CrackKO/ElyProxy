using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ElyProxy.Services;

namespace ElyProxy.Controls;

/// <summary>
/// Apple-style toast stack: material cards that slide in from the right edge
/// and dismiss themselves. Replaces the Fluent Snackbar so every surface in the
/// window is drawn by our own design system.
/// </summary>
public partial class ToastHost : UserControl
{
    private const int MaxVisible = 3;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);

    private readonly ObservableCollection<Toast> _toasts = new();

    public ToastHost()
    {
        InitializeComponent();
        Items.ItemsSource = _toasts;
    }

    public void Show(string title, string message, NoticeKind kind)
    {
        while (_toasts.Count >= MaxVisible)
        {
            _toasts.RemoveAt(0);
        }

        var toast = new Toast(title, message, kind, ResolveAccent(kind));
        _toasts.Add(toast);

        var timer = new DispatcherTimer { Interval = Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Dismiss(toast);
        };
        timer.Start();
    }

    private static Brush ResolveAccent(NoticeKind kind)
    {
        var key = kind switch
        {
            NoticeKind.Success => "GreenBrush",
            NoticeKind.Warning => "OrangeBrush",
            NoticeKind.Error => "RedBrush",
            _ => "AccentBrush"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.DodgerBlue;
    }

    private void Dismiss(Toast toast)
    {
        if (Items.ItemContainerGenerator.ContainerFromItem(toast) is FrameworkElement container)
        {
            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) => _toasts.Remove(toast);
            container.BeginAnimation(OpacityProperty, fade);
            return;
        }

        _toasts.Remove(toast);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Toast toast)
        {
            Dismiss(toast);
        }
    }
}

public sealed record Toast(string Title, string Message, NoticeKind Kind, Brush Accent);
