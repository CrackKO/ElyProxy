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

    private bool IsParameters => InputMode?.SelectedIndex == 1;
    private string _parameterError = string.Empty;
    public bool IsValid => (!IsParameters || string.IsNullOrEmpty(_parameterError)) && IsValidUri(Uri);

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LinkPanel == null || ParametersPanel == null) return;
        LinkPanel.Visibility = IsParameters ? Visibility.Collapsed : Visibility.Visible;
        ParametersPanel.Visibility = IsParameters ? Visibility.Visible : Visibility.Collapsed;
        if (IsParameters) UpdateParameters();
        else NotifyValidation();
    }

    private void Parameters_Changed(object sender, RoutedEventArgs e)
    {
        if (IsParameters && NameBox != null) UpdateParameters();
    }

    private void UpdateParameters()
    {
        _parameterError = string.Empty;
        var host = HostBox.Text.Trim().Trim('[', ']');
        if (System.Uri.CheckHostName(host) == UriHostNameType.Unknown)
            _parameterError = "Укажите корректный IP-адрес или домен без схемы и порта.";
        else if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
            _parameterError = "Порт должен быть числом от 1 до 65535.";
        else if (UsernameBox.Text.Length == 0 && SecretBox.Password.Length > 0)
            _parameterError = "Укажите логин для подключения с паролем.";
        if (_parameterError.Length > 0) { Uri = string.Empty; NotifyValidation(); return; }
        var scheme = ProxyType.SelectedIndex switch { 1 => "http", 2 => "https", _ => "socks5" };
        if (host.Contains(':')) host = $"[{host}]";
        var auth = UsernameBox.Text.Length > 0
            ? $"{System.Uri.EscapeDataString(UsernameBox.Text)}:{System.Uri.EscapeDataString(SecretBox.Password)}@"
            : string.Empty;
        Uri = $"{scheme}://{auth}{host}:{PortBox.Text.Trim()}#{System.Uri.EscapeDataString(NameBox.Text.Trim())}";
        NotifyValidation();
    }

    private void NotifyValidation()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValid)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UriError)));
    }

    public string UriError =>
        IsParameters ? _parameterError : string.IsNullOrWhiteSpace(Uri) || IsValidUri(Uri)
            ? string.Empty
            : "Нужна корректная ссылка vless://, vmess://, trojan://, ss://, hysteria2://, tuic://, anytls://, ssh://, socks5://, socks://, http:// или https://.";

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var sheet = (AddServerSheet)d;
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(IsValid)));
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(UriError)));
    }

    private static bool IsValidUri(string value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        try
        {
            return Parser.ParseUri(candidate) is not null;
        }
        catch
        {
            return false;
        }
    }
}
