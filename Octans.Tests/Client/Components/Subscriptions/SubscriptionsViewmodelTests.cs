using FluentAssertions;
using MudBlazor;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Subscriptions;
using Octans.Core.Downloaders;
using Octans.Core.Subscriptions;
using Octans.Tests.Helpers;

namespace Octans.Tests.Client.Components.Subscriptions;

public class SubscriptionsViewmodelTests
{
    private readonly IOctansClient _client = Substitute.For<IOctansClient>();
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
    private readonly SubscriptionsViewmodel _sut;

    public SubscriptionsViewmodelTests()
    {
        _sut = new(_client, _dialogService);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadSubscriptions()
    {
        var subscription = CreateSubscription();
        _client.GetSubscriptionsAsync().Returns([subscription]);

        await _sut.InitializeAsync();

        _sut.Subscriptions.Should().ContainSingle().Which.Should().Be(subscription);
    }

    [Fact]
    public async Task AddSubscriptionAsync_ShouldAddSubscription_WhenDialogConfirmed()
    {
        _client.GetDownloadersAsync().Returns([new DownloaderMetadata { Name = "TestDownloader" }]);
        _client.GetSubscriptionsAsync().Returns(
            [CreateSubscription(name: "NewSub", downloaderName: "TestDownloader", query: "NewQuery")]);

        var dialogReference = Substitute.For<IDialogReference>();
        var formModel = new AddSubscriptionDialog.FormModel
        {
            Name = "NewSub",
            Downloader = "TestDownloader",
            Query = "NewQuery",
            FrequencyMinutes = 30
        };
        dialogReference.Result.Returns(Task.FromResult<DialogResult?>(DialogResult.Ok(formModel)));
        _dialogService.ShowAsync<AddSubscriptionDialog>(
                Arg.Any<string>(),
                Arg.Any<DialogParameters<AddSubscriptionDialog>>())
            .Returns(Task.FromResult(dialogReference));

        await _sut.AddSubscriptionAsync();

        await _client
            .Received(1)
            .AddSubscriptionAsync(Arg.Is<SubscriptionCreateRequest>(r =>
                r.Name == "NewSub"
                && r.DownloaderName == "TestDownloader"
                && r.Query == "NewQuery"
                && r.FrequencyMinutes == 30));
        _sut.Subscriptions.Should().ContainSingle(s => s.Name == "NewSub");
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_ShouldRemoveSubscription()
    {
        _client.GetSubscriptionsAsync().Returns([CreateSubscription()]);

        await _sut.InitializeAsync();
        _client.GetSubscriptionsAsync().Returns([]);
        await _sut.DeleteSubscriptionAsync(7);

        await _client.Received(1).DeleteSubscriptionAsync(7);
        _sut.Subscriptions.Should().BeEmpty();
    }

    private static SubscriptionStatusDto CreateSubscription(
        int id = 7,
        string name = "TestSub",
        string downloaderName = "TestDownloader",
        string query = "TestQuery") =>
        new(
            id,
            name,
            downloaderName,
            query,
            TimeSpan.FromMinutes(60),
            null,
            null,
            TestClock.UtcNow);
}
