using System.Windows;
using System.Windows.Controls;

namespace ElyProxy.Controls;

/// <summary>
/// Placeholder rows shown while a list loads for the first time. Keeps the
/// layout stable instead of flashing an empty state.
/// </summary>
public partial class SkeletonList : UserControl
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows),
        typeof(int),
        typeof(SkeletonList),
        new PropertyMetadata(5, OnRowsChanged));

    public SkeletonList()
    {
        InitializeComponent();
        UpdateRows();
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SkeletonList)d).UpdateRows();

    private void UpdateRows() => RowsHost.ItemsSource = Enumerable.Range(0, Math.Max(0, Rows));
}
