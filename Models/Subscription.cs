using System.ComponentModel;
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

    [JsonIgnore]
    public string LastUpdateStatus => !string.IsNullOrWhiteSpace(LastUpdateError)
        ? "Ошибка: " + LastUpdateError + (LastUpdated == default ? string.Empty : $" · последний успех {LastUpdated:dd.MM.yyyy HH:mm}")
        : LastUpdated == default
            ? "Ещё не обновлялась"
            : $"Успешно {LastUpdated:dd.MM.yyyy HH:mm}";

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
