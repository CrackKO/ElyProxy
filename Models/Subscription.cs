using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ElyProxy.Models;

public sealed class Subscription : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _url = string.Empty;
    private DateTime _lastUpdated;
    private List<VlessNode> _nodes = new();
    private string? _lastUpdateError;
    private long _uploadBytes;
    private long _downloadBytes;
    private long _totalBytes;
    private DateTimeOffset? _expiresAt;
    private string _userAgent = string.Empty;
    private int _updateIntervalMinutes;
    private string _serverFilter = string.Empty;
    private string _sortMode = "Default";

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Url
    {
        get => _url;
        set => SetField(ref _url, value);
    }

    public DateTime LastUpdated
    {
        get => _lastUpdated;
        set
        {
            if (SetField(ref _lastUpdated, value))
            {
                OnPropertyChanged(nameof(LastUpdateStatus));
            }
        }
    }

    public List<VlessNode> Nodes
    {
        get => _nodes;
        set
        {
            if (SetField(ref _nodes, value ?? new List<VlessNode>()))
            {
                OnPropertyChanged(nameof(LastUpdateStatus));
            }
        }
    }

    public string? LastUpdateError
    {
        get => _lastUpdateError;
        set
        {
            if (SetField(ref _lastUpdateError, value))
            {
                OnPropertyChanged(nameof(LastUpdateStatus));
            }
        }
    }

    public long UploadBytes
    {
        get => _uploadBytes;
        set
        {
            if (SetField(ref _uploadBytes, value))
            {
                OnPropertyChanged(nameof(TrafficSummary));
                OnPropertyChanged(nameof(HasTrafficInfo));
            }
        }
    }

    public long DownloadBytes
    {
        get => _downloadBytes;
        set
        {
            if (SetField(ref _downloadBytes, value))
            {
                OnPropertyChanged(nameof(TrafficSummary));
                OnPropertyChanged(nameof(HasTrafficInfo));
            }
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (SetField(ref _totalBytes, value))
            {
                OnPropertyChanged(nameof(TrafficSummary));
                OnPropertyChanged(nameof(HasTrafficInfo));
            }
        }
    }

    public DateTimeOffset? ExpiresAt
    {
        get => _expiresAt;
        set
        {
            if (SetField(ref _expiresAt, value))
            {
                OnPropertyChanged(nameof(TrafficSummary));
                OnPropertyChanged(nameof(HasTrafficInfo));
            }
        }
    }

    public string UserAgent
    {
        get => _userAgent;
        set => SetField(ref _userAgent, value ?? string.Empty);
    }

    public int UpdateIntervalMinutes
    {
        get => _updateIntervalMinutes;
        set => SetField(ref _updateIntervalMinutes, value);
    }

    public string ServerFilter
    {
        get => _serverFilter;
        set => SetField(ref _serverFilter, value ?? string.Empty);
    }

    public string SortMode
    {
        get => _sortMode;
        set => SetField(ref _sortMode, value ?? "Default");
    }

    public string Hwid { get; set; } = string.Empty;

    public Dictionary<string, string> CustomHeaders { get; set; } = new Dictionary<string, string>();

    public bool TrafficWarningShown { get; set; }

    public bool ExpiryWarningShown { get; set; }

    [JsonIgnore]
    public bool HasTrafficInfo => TotalBytes > 0 || UploadBytes > 0 || DownloadBytes > 0 || ExpiresAt.HasValue;

    [JsonIgnore]
    public string TrafficSummary
    {
        get
        {
            if (!HasTrafficInfo)
            {
                return string.Empty;
            }

            string used = Services.TrafficFormatter.FormatBytes(UploadBytes + DownloadBytes);
            string volume = TotalBytes > 0
                ? $"{used} из {Services.TrafficFormatter.FormatBytes(TotalBytes)}"
                : used;
            string expires = ExpiresAt.HasValue
                ? $" · до {ExpiresAt.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
                : string.Empty;
            return volume + expires;
        }
    }

    [JsonIgnore]
    public string LastUpdateStatus => !string.IsNullOrWhiteSpace(LastUpdateError)
        ? "Ошибка: " + LastUpdateError + (LastUpdated == default ? string.Empty : $" · последний успех {LastUpdated.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}")
        : LastUpdated == default
            ? "Ещё не обновлялась"
            : $"Успешно {LastUpdated.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
