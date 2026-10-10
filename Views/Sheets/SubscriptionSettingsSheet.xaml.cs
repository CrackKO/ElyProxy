using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ElyProxy.Models;
using ElyProxy.Services;
namespace ElyProxy.Views.Sheets;

public partial class SubscriptionSettingsSheet : UserControl, INotifyPropertyChanged, ISheetValidation
{
    private string _userAgent = string.Empty;
    private string _updateIntervalMinutes = "0";
    private string _serverFilter = string.Empty;
    private int _sortModeIndex;
    private string _hwid = string.Empty;
    private string _customHeadersText = string.Empty;
    private bool _loadingPresets;

    public SubscriptionSettingsSheet(Subscription subscription)
    {
        InitializeComponent();
        _userAgent = subscription.UserAgent;
        _updateIntervalMinutes = subscription.UpdateIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        _serverFilter = subscription.ServerFilter;
        _sortModeIndex = subscription.SortMode switch
        {
            "Name" => 1,
            "Latency" => 2,
            _ => 0
        };
        _hwid = subscription.Hwid;
        _customHeadersText = SubscriptionHeaderParser.Format(subscription.CustomHeaders);
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

    public string UserAgent
    {
        get => _userAgent;
        set => SetField(ref _userAgent, value, nameof(UserAgent));
    }

    public string UpdateIntervalMinutes
    {
        get => _updateIntervalMinutes;
        set
        {
            if (SetField(ref _updateIntervalMinutes, value, nameof(UpdateIntervalMinutes)))
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

    private void PresetBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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

    public bool IsValid => TryGetInterval(out _);

    public string ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(UpdateIntervalMinutes))
            {
                return string.Empty;
            }
            return TryGetInterval(out _)
                ? string.Empty
                : "Интервал должен быть числом от 0 до 10080 минут.";
        }
    }

    public void ApplyTo(Subscription subscription)
    {
        subscription.UserAgent = UserAgent.Trim();
        TryGetInterval(out int interval);
        subscription.UpdateIntervalMinutes = interval;
        subscription.ServerFilter = ServerFilter.Trim();
        subscription.SortMode = SortModeIndex switch
        {
            1 => "Name",
            2 => "Latency",
            _ => "Default"
        };
        subscription.Hwid = Hwid.Trim();
        subscription.CustomHeaders = SubscriptionHeaderParser.Parse(CustomHeadersText);
    }

    private bool TryGetInterval(out int interval)
    {
        interval = 0;
        string value = UpdateIntervalMinutes.Trim();
        if (value.Length == 0)
        {
            return true;
        }
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out interval)
            && interval >= 0
            && interval <= 10080;
    }

    private void RaiseValidationChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValid)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationError)));
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
}
