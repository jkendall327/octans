using Octans.Core.Downloaders;
using Octans.Core.Subscriptions;
using MudBlazor;

namespace Octans.Client.Components.Subscriptions;

public class SubscriptionsViewmodel(
    SubscriptionService subscriptionService,
    DownloaderFactory downloaderFactory,
    IDialogService dialogService)
{
    public List<SubscriptionStatusDto> Subscriptions { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await LoadSubscriptionsAsync();
    }

    private async Task LoadSubscriptionsAsync()
    {
        Subscriptions = await subscriptionService.GetAllAsync();
    }

    public async Task AddSubscriptionAsync()
    {
        var downloaders = await downloaderFactory.GetDownloaders();
        var downloaderNames = downloaders.Select(d => d.Metadata.Name).OrderBy(n => n).ToList();

        var parameters = new DialogParameters<AddSubscriptionDialog>
        {
            { x => x.DownloaderNames, downloaderNames }
        };

        var dialog = await dialogService.ShowAsync<AddSubscriptionDialog>("Add Subscription", parameters);
        var result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is AddSubscriptionDialog.FormModel model)
        {
            await subscriptionService.AddAsync(
                model.Name,
                model.Downloader,
                model.Query,
                TimeSpan.FromMinutes(model.FrequencyMinutes));
            await LoadSubscriptionsAsync();
        }
    }

    public async Task DeleteSubscriptionAsync(int id)
    {
        await subscriptionService.DeleteAsync(id);
        await LoadSubscriptionsAsync();
    }
}
