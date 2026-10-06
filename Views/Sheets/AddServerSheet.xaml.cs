using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ElyProxy.Services;

namespace ElyProxy.Views.Sheets;

public partial class AddServerSheet : UserControl, INotifyPropertyChanged, ISheetValidation
{
    private static readonly ParserService Parser = new();

    public static readonly DependencyProperty UriProperty = DependencyProperty.Register(
        nameof(Uri),
        typeof(string),
        typeof(AddServerSheet),
        new PropertyMetadata(string.Empty, OnValueChanged));

    public AddServerSheet()
    {
        InitializeComponent();
        Loaded += (_, _) => UriBox.Focus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Uri
    {
        get => (string)GetValue(UriProperty);
        set => SetValue(UriProperty, value);
    }

    public bool IsValid => IsValidVless(Uri);

    public string UriError =>
        string.IsNullOrWhiteSpace(Uri) || IsValidVless(Uri)
            ? string.Empty
            : "Нужна корректная ссылка vless:// с адресом, портом и UUID.";

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var sheet = (AddServerSheet)d;
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(IsValid)));
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(UriError)));
    }

    private static bool IsValidVless(string value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate) || !candidate.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return Parser.ParseVlessUri(candidate) is not null;
        }
        catch
        {
            return false;
        }
    }
}
