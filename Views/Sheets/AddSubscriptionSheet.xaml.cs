using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ElyProxy.Services;

namespace ElyProxy.Views.Sheets;

public partial class AddSubscriptionSheet : UserControl, INotifyPropertyChanged, ISheetValidation
{
    public static readonly DependencyProperty SubscriptionNameProperty = DependencyProperty.Register(
        nameof(SubscriptionName),
        typeof(string),
        typeof(AddSubscriptionSheet),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubscriptionUrlProperty = DependencyProperty.Register(
        nameof(SubscriptionUrl),
        typeof(string),
        typeof(AddSubscriptionSheet),
        new PropertyMetadata(string.Empty, OnUrlChanged));

    public AddSubscriptionSheet()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SubscriptionName
    {
        get => (string)GetValue(SubscriptionNameProperty);
        set => SetValue(SubscriptionNameProperty, value);
    }

    public string SubscriptionUrl
    {
        get => (string)GetValue(SubscriptionUrlProperty);
        set => SetValue(SubscriptionUrlProperty, value);
    }

    public bool IsValid => IsValidUrl(SubscriptionUrl);

    public string UrlError =>
        string.IsNullOrWhiteSpace(SubscriptionUrl) || IsValidUrl(SubscriptionUrl)
            ? string.Empty
            : "Ссылка должна начинаться с http:// или https://";

    private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var sheet = (AddSubscriptionSheet)d;
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(IsValid)));
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(UrlError)));
    }

    private static bool IsValidUrl(string value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
