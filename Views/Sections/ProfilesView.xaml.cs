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
