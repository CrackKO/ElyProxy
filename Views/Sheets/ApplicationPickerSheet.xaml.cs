using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using ElyProxy.Models;
using ElyProxy.Services;
using Microsoft.Win32;

namespace ElyProxy.Views.Sheets;

public partial class ApplicationPickerSheet : UserControl, INotifyPropertyChanged, ISheetValidation
{
    private readonly RunningApplicationService _applicationService;
    private readonly string[] _excludedPaths;
    private readonly List<RunningApplication> _allApplications = new();
    private string _searchText = string.Empty;
    private RunningApplication? _selectedApplication;

    public ObservableCollection<RunningApplication> FilteredApplications { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public RunningApplication? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (ReferenceEquals(_selectedApplication, value))
            {
                return;
            }

            _selectedApplication = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
        }
    }

    public bool IsValid => SelectedApplication != null;

    public ApplicationPickerSheet(RunningApplicationService applicationService, System.Collections.Generic.IEnumerable<string> excludedPaths)
    {
        _applicationService = applicationService;
        _excludedPaths = excludedPaths.ToArray();
        InitializeComponent();
        RefreshApplications();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text.Trim() ?? string.Empty;
        ApplyFilter();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshApplications();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Title = "Выбрать приложение",
            Filter = "Программы Windows (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        RunningApplication application = RunningApplication.FromPath(RunningApplicationService.NormalizePath(dialog.FileName));
        if (!_allApplications.Any(existing => string.Equals(existing.ExecutablePath, application.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        {
            _allApplications.Insert(0, application);
        }

        SearchBox.Clear();
        ApplyFilter();
        SelectedApplication = FilteredApplications.FirstOrDefault(existing =>
            string.Equals(existing.ExecutablePath, application.ExecutablePath, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshApplications()
    {
        _allApplications.Clear();
        _allApplications.AddRange(_applicationService.GetVisibleApplications(_excludedPaths));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = _searchText;
        var applications = _allApplications
            .Where(application => query.Length == 0
                || application.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || application.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        string? selectedPath = SelectedApplication?.ExecutablePath;
        FilteredApplications.Clear();
        foreach (RunningApplication application in applications)
        {
            FilteredApplications.Add(application);
        }

        SelectedApplication = selectedPath == null
            ? null
            : FilteredApplications.FirstOrDefault(application => string.Equals(
                application.ExecutablePath,
                selectedPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
