using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Views.Sheets;

public partial class AddSubscriptionSheet : UserControl, INotifyPropertyChanged, ISheetValidation
{
    private string _userAgent = string.Empty;
    private string _updateIntervalText = "0";
    private string _serverFilter = string.Empty;
    private int _sortModeIndex;
    private string _hwid = string.Empty;
    private string _customHeadersText = string.Empty;
    private bool _loadingPresets;

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
        _loadingPresets = true;
        PresetBox.ItemsSource = UserAgentCatalog.Presets;
        PresetBox.SelectedIndex = FindPresetIndex(UserAgent);
        _loadingPresets = false;
    }

    private static int FindPresetIndex(string userAgent)
    {
        for (int index = 0; index < UserAgentCatalog.Presets.Count; index++)
        {
            if (string.Equals(UserAgentCatalog.Presets[index].Value, userAgent, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        return 0;
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

    public string UserAgent
    {
        get => _userAgent;
        set => SetField(ref _userAgent, value, nameof(UserAgent));
    }

    public string UpdateIntervalText
    {
        get => _updateIntervalText;
        set
        {
            if (SetField(ref _updateIntervalText, value, nameof(UpdateIntervalText)))
            {
                RaiseValidationChanged();
            }
        }
    }

    public string ServerFilter
    {
        get => _serverFilter;
        set => SetField(ref _serverFilter, value, nameof(ServerFilter));
    }

    public int SortModeIndex
    {
        get => _sortModeIndex;
        set => SetField(ref _sortModeIndex, value, nameof(SortModeIndex));
    }

    public int UpdateIntervalMinutes
    {
        get
        {
            TryGetInterval(out int interval);
            return interval;
        }
    }

    public string SortMode => SortModeIndex switch
    {
        1 => "Name",
        2 => "Latency",
        _ => "Default"
    };

    public string Hwid
    {
        get => _hwid;
        set => SetField(ref _hwid, value, nameof(Hwid));
    }

    public string CustomHeadersText
    {
        get => _customHeadersText;
        set => SetField(ref _customHeadersText, value, nameof(CustomHeadersText));
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPresets)
        {
            return;
        }
        if (PresetBox.SelectedItem is UserAgentPreset preset)
        {
            UserAgent = preset.Value;
        }
    }

    private void GenerateHwid_Click(object sender, RoutedEventArgs e)
    {
        Hwid = Guid.NewGuid().ToString("N");
    }

    public bool IsValid => IsValidUrl(SubscriptionUrl) && TryGetInterval(out _);

    public string UrlError =>
        string.IsNullOrWhiteSpace(SubscriptionUrl) || IsValidUrl(SubscriptionUrl)
            ? string.Empty
            : "Ссылка должна начинаться с http:// или https://";

    public string IntervalError =>
        TryGetInterval(out _)
            ? string.Empty
            : "Интервал должен быть числом от 0 до 10080 минут.";

    private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var sheet = (AddSubscriptionSheet)d;
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(IsValid)));
        sheet.PropertyChanged?.Invoke(sheet, new PropertyChangedEventArgs(nameof(UrlError)));
    }

    private void RaiseValidationChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValid)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntervalError)));
    }

    private bool TryGetInterval(out int interval)
    {
        interval = 0;
        string value = (UpdateIntervalText ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return true;
        }
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out interval)
            && interval >= 0
            && interval <= 10080;
    }

    private bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private static bool IsValidUrl(string value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
