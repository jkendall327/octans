using MudBlazor;
using Octans.Core.Subscriptions;

namespace Octans.Client.Components.Subscriptions;

public class SubscriptionsViewmodel(
    IOctansClient client,
    IDialogService dialogService)
{
    public List<SubscriptionStatusDto> Subscriptions { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await LoadSubscriptionsAsync();
    }

    private async Task LoadSubscriptionsAsync()
    {
        Subscriptions = (await client.GetSubscriptionsAsync()).ToList();
    }

    public async Task AddSubscriptionAsync()
    {
        var downloaders = await client.GetDownloadersAsync();
        var downloaderNames = downloaders.Select(d => d.Name).OrderBy(n => n).ToList();

        var parameters = new DialogParameters<AddSubscriptionDialog>
        {
            { x => x.DownloaderNames, downloaderNames }
        };

        var dialog = await dialogService.ShowAsync<AddSubscriptionDialog>("Add Subscription", parameters);
        var result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is AddSubscriptionDialog.FormModel model)
        {
            await client.AddSubscriptionAsync(new(
                model.Name,
                model.Downloader,
                model.Query,
                model.FrequencyMinutes,
                new(model.Repository, model.AllowReimportDeleted, model.AutoArchive),
                model.Tags,
                model.MaxItemsPerRun));
            await LoadSubscriptionsAsync();
        }
    }

    public async Task DeleteSubscriptionAsync(int id)
    {
        await client.DeleteSubscriptionAsync(id);
        await LoadSubscriptionsAsync();
    }

    public async Task RunSubscriptionAsync(int id)
    {
        await client.RunSubscriptionNowAsync(id);
        await LoadSubscriptionsAsync();
    }

    public async Task ToggleSubscriptionAsync(SubscriptionStatusDto subscription)
    {
        await client.SetSubscriptionEnabledAsync(subscription.Id, !subscription.IsEnabled);
        await LoadSubscriptionsAsync();
    }

    public async Task ShowHistoryAsync(int id)
    {
        var parameters = new DialogParameters<SubscriptionHistoryDialog>
        {
            { dialog => dialog.SubscriptionId, id }
        };
        await dialogService.ShowAsync<SubscriptionHistoryDialog>("Subscription history", parameters);
    }
}
