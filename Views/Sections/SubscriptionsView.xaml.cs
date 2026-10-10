using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElyProxy.Models;
using ElyProxy.Services;
using ElyProxy.ViewModels;
using ElyProxy.Views.Sheets;

namespace ElyProxy.Views.Sections;

public partial class SubscriptionsView : UserControl
{
    public SubscriptionsView()
    {
        InitializeComponent();
        EmptySubscriptions.ActionCommand = new RelayCommand(() => _ = AddSubscriptionAsync());
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void AddSubscription_Click(object sender, RoutedEventArgs e) => await AddSubscriptionAsync();

    private async Task AddSubscriptionAsync()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var sheet = new AddSubscriptionSheet();
        if (!await DialogService.Current.ShowCustomAsync("Новая подписка", sheet, "Добавить"))
        {
            return;
        }

        if (!sheet.IsValid)
        {
            return;
        }

        viewModel.NewSubName = sheet.SubscriptionName.Trim();
        viewModel.NewSubUrl = sheet.SubscriptionUrl.Trim();
        viewModel.NewSubUserAgent = sheet.UserAgent;
        viewModel.NewSubUpdateIntervalMinutes = sheet.UpdateIntervalMinutes;
        viewModel.NewSubServerFilter = sheet.ServerFilter;
        viewModel.NewSubSortMode = sheet.SortMode;
        viewModel.NewSubHwid = sheet.Hwid;
        viewModel.NewSubCustomHeadersText = sheet.CustomHeadersText;
        viewModel.AddSubscriptionCommand.Execute(null);
    }

    private async void DeleteSubscription_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedSubscription is not { } subscription)
        {
            return;
        }

        await DeleteSubscriptionAsync(subscription);
    }

    private void RowUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || ResolveSubscription(sender) is not { } subscription)
        {
            return;
        }

        viewModel.SelectedSubscription = subscription;
        viewModel.UpdateSubscriptionCommand.Execute(null);
    }

    private async void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || ResolveSubscription(sender) is not { } subscription)
        {
            return;
        }

        viewModel.SelectedSubscription = subscription;
        await DeleteSubscriptionAsync(subscription);
    }

    private async void RowSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || ResolveSubscription(sender) is not { } subscription)
        {
            return;
        }

        var sheet = new SubscriptionSettingsSheet(subscription);
        if (!await DialogService.Current.ShowCustomAsync("Настройки подписки", sheet, "Сохранить") || !sheet.IsValid)
        {
            return;
        }

        sheet.ApplyTo(subscription);
        await viewModel.ApplySubscriptionSettingsAsync(subscription);
    }

    private void SubscriptionList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView list
            && ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) is ListViewItem item)
        {
            item.IsSelected = true;
        }
    }

    private async Task DeleteSubscriptionAsync(Subscription subscription)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var confirmed = await DialogService.Current.ConfirmAsync(
            "Удалить подписку?",
            $"«{subscription.Name}» и её серверы будут удалены. Действие можно отменить только повторным добавлением ссылки.",
            "Удалить",
            "Отмена",
            destructive: true);

        if (confirmed)
        {
            viewModel.RemoveSubscriptionCommand.Execute(null);
        }
    }

    private static Subscription? ResolveSubscription(object sender) =>
        (sender as FrameworkElement)?.DataContext as Subscription;
}
