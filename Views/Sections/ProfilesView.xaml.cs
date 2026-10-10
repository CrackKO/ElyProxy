using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElyProxy.Models;
using ElyProxy.Services;
using ElyProxy.ViewModels;

namespace ElyProxy.Views.Sections;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ExportClash_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProfile is not { } profile)
        {
            return;
        }

        SaveTextFile(
            "Экспорт Clash YAML",
            $"{SanitizeFileName(profile.Name)}-clash.yaml",
            "YAML (*.yaml)|*.yaml|Все файлы (*.*)|*.*",
            () => ClashYamlExporter.Export(profile.Nodes));
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProfile is not { } profile)
        {
            return;
        }

        SaveTextFile(
            "Экспорт JSON (Xray outbounds)",
            $"{SanitizeFileName(profile.Name)}-xray.json",
            "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            () => V2rayNJsonExporter.Export(profile.Nodes));
    }

    private async void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedProfile is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Импорт серверов в профиль",
            Filter = "Списки серверов (*.yaml;*.yml;*.json;*.txt)|*.yaml;*.yml;*.json;*.txt|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string content = File.ReadAllText(dialog.FileName);
            var nodes = new ParserService().ParseSubscriptionContent(content);
            if (nodes.Count == 0)
            {
                DialogService.Current.Notify("Серверы не найдены", "В файле не найден ни один поддерживаемый сервер (ссылки, Clash YAML или JSON).", NoticeKind.Warning);
                return;
            }

            int added = await viewModel.ImportNodesToSelectedProfileAsync(nodes);
            DialogService.Current.Notify(
                added > 0 ? "Импорт завершён" : "Новых серверов нет",
                added > 0 ? $"Добавлено в «{viewModel.SelectedProfile.Name}»: {added}. Дубликаты пропущены." : "Все серверы из файла уже есть в профиле.",
                added > 0 ? NoticeKind.Success : NoticeKind.Warning);
        }
        catch (Exception ex)
        {
            DialogService.Current.Notify("Ошибка импорта", ex.Message, NoticeKind.Error);
        }
    }

    private static void SaveTextFile(string title, string defaultName, string filter, Func<string> contentFactory)    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            FileName = defaultName,
            Filter = filter
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, contentFactory());
            DialogService.Current.Notify("Экспорт завершён", dialog.FileName, NoticeKind.Success);
        }
        catch (Exception ex)
        {
            DialogService.Current.Notify("Не удалось экспортировать", ex.Message, NoticeKind.Error);
        }
    }

    private static string SanitizeFileName(string name)
    {
        string sanitized = name;
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }
        return (string.IsNullOrWhiteSpace(sanitized) ? "profile" : sanitized.Trim());
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedProfile is not { } profile)
        {
            return;
        }

        await DeleteProfileAsync(profile);
    }

    private void DeleteProfileRow_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || Resolve<ProxyProfile>(sender) is not { } profile)
        {
            return;
        }

        viewModel.SelectedProfile = profile;
        _ = DeleteProfileAsync(profile);
    }

    private async void ClearProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedProfile is not { } profile)
        {
            return;
        }

        var confirmed = await DialogService.Current.ConfirmAsync(
            "Очистить профиль?",
            $"Все серверы будут убраны из профиля «{profile.Name}».",
            "Очистить",
            "Отмена",
            destructive: true);

        if (confirmed)
        {
            viewModel.ClearProfileCommand.Execute(null);
        }
    }

    private void ProfilesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        SelectRowUnderCursor(sender, e);

    private void ProfileNodesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        SelectRowUnderCursor(sender, e);

    private async Task DeleteProfileAsync(ProxyProfile profile)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var confirmed = await DialogService.Current.ConfirmAsync(
            "Удалить профиль?",
            $"Профиль «{profile.Name}» будет удалён. Серверы в подписках останутся на месте.",
            "Удалить",
            "Отмена",
            destructive: true);

        if (confirmed)
        {
            viewModel.DeleteProfileCommand.Execute(null);
        }
    }

    private static void SelectRowUnderCursor(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list
            && ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    private static T? Resolve<T>(object sender) where T : class =>
        (sender as FrameworkElement)?.DataContext as T;
}
