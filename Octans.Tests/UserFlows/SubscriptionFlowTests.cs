using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Octans.Client;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Http;
using Octans.Core.Subscriptions;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Subscriptions;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.UserFlows;

public sealed class SubscriptionFlowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UserCan_RunSubscriptionImportDiscoveredItemsAvoidAlreadySeenItemsAndInspectHistory()
    {
        var discoveredItems = CreateDiscoveredItems();
        var executor = new FakeSubscriptionExecutor(discoveredItems);

        await using var factory = new OctansApiFactory(
            output,
            services => services.ReplaceExistingRegistrationsWith<ISubscriptionExecutor>(executor));
        var client = factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/subscriptions", UriKind.Relative),
            new
            {
                Name = "Octans flow subscription",
                DownloaderName = "fake-gallery-downloader",
                Query = "artist:octans",
                FrequencyMinutes = 30,
                ImportOptions = new
                {
                    Repository = "Inbox",
                    AllowReimportDeleted = false,
                    AutoArchive = false
                },
                Tags = new[]
                {
                    new TagModel("series", "octans subscription"),
                    new TagModel("source", "fake-gallery-downloader")
                }
            },
            OctansApiFactory.JsonOptions);
        var subscriptionsAfterCreate = await GetSubscriptions(client);

        await RunDueSubscriptions(factory);

        var executionsAfterFirstRun = executor.Executions.ToList();
        var subscriptionsAfterFirstRun = await GetSubscriptions(client);
        var downloadsAfterFirstRun = await GetDownloads(client);
        var importJobsAfterFirstRun = await GetImportJobs(client);
        var subscriptionTagResults = await OctansApiFactory.QueryAsync(client, ["series:octans subscription"]);
        var mediaAfterFirstRun = await ProbeMedia(client, discoveredItems);

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(30));
        await RunDueSubscriptions(factory);

        var subscriptionsAfterSecondRun = await GetSubscriptions(client);
        var downloadsAfterSecondRun = await GetDownloads(client);
        var importJobsAfterSecondRun = await GetImportJobs(client);

        using var subscriptionStory = new AssertionScope("subscription story");

        using (new AssertionScope("The user can create and list the subscription"))
        {
            createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            subscriptionsAfterCreate.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            subscriptionsAfterCreate.Items.Should().ContainSingle(subscription =>
                subscription.Name == "Octans flow subscription"
                && subscription.DownloaderName == "fake-gallery-downloader"
                && subscription.Query == "artist:octans");
        }

        using (new AssertionScope("A due run records a visible execution result"))
        {
            executionsAfterFirstRun.Should().ContainSingle(execution =>
                execution.SubscriptionName == "Octans flow subscription"
                && execution.Query == "artist:octans");
            subscriptionsAfterFirstRun.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            subscriptionsAfterFirstRun.Items.Should().ContainSingle(subscription =>
                subscription.Name == "Octans flow subscription"
                && subscription.LastRun != null
                && subscription.ItemsFound == discoveredItems.Count);
        }

        using (new AssertionScope("Discovered remote items become durable subscription-owned work"))
        {
            downloadsAfterFirstRun.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            downloadsAfterFirstRun.Items
                .Select(download => download.Url)
                .Should()
                .Contain(discoveredItems.Select(item => item.RemoteUrl.ToString()));
            downloadsAfterFirstRun.Items
                .Where(download => discoveredItems.Any(item => item.RemoteUrl.ToString() == download.Url))
                .Should()
                .OnlyContain(download =>
                    download.SourceType == "Subscription"
                    && !string.IsNullOrWhiteSpace(download.SourceId));

            importJobsAfterFirstRun.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            importJobsAfterFirstRun.Items
                .SelectMany(job => job.Items)
                .Should()
                .Contain(item =>
                    discoveredItems.Any(discovered => discovered.RemoteUrl.ToString() == item.Source)
                    && item.ImportType == nameof(ImportType.RawUrl));
        }

        using (new AssertionScope("Processing subscription work makes media usable through normal library APIs"))
        {
            subscriptionTagResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            subscriptionTagResults.Hashes.Should().BeEquivalentTo(discoveredItems.Select(item => item.Hash.Hex));

            foreach (var probe in mediaAfterFirstRun)
            {
                probe.DetailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                probe.Details.Should().Match<MediaDetailsDto>(details =>
                    details.Hash == probe.Item.Hash.Hex
                    && details.Repository == RepositoryType.Inbox
                    && details.ContentType == "image/jpeg"
                    && details.MediaUrl == $"/media/{probe.Item.Hash.Hex}");
                probe.Details?.Tags.Should().Contain(tag =>
                    tag.Namespace == "series" && tag.Subtag == "octans subscription");
                probe.MediaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                probe.MediaResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
                probe.MediaBytes.Should().Equal(probe.Item.Bytes);
            }
        }

        using (new AssertionScope("A later run recognises already-seen source items before redownloading"))
        {
            executor.Executions.Should().HaveCount(2);
            subscriptionsAfterSecondRun.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            subscriptionsAfterSecondRun.Items.Should().ContainSingle(subscription =>
                subscription.Name == "Octans flow subscription"
                && subscription.LastRun != null);
            CountSubscriptionDownloads(downloadsAfterSecondRun.Items, discoveredItems)
                .Should()
                .Be(discoveredItems.Count);
            CountSubscriptionImportItems(importJobsAfterSecondRun.Items, discoveredItems)
                .Should()
                .Be(discoveredItems.Count);
        }
    }

    [Fact]
    public async Task UserCan_SeeSubscriptionFailureHistoryAndFutureRunsStillRecover()
    {
        var discoveredItems = CreateDiscoveredItems();
        var executor = new FakeSubscriptionExecutor(discoveredItems)
        {
            FailNextExecution = true
        };

        await using var factory = new OctansApiFactory(
            output,
            services => services.ReplaceExistingRegistrationsWith<ISubscriptionExecutor>(executor));
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/subscriptions", UriKind.Relative),
            new SubscriptionCreateRequest(
                "Flaky subscription",
                "fake-gallery-downloader",
                "gallery:flaky",
                5),
            OctansApiFactory.JsonOptions);

        var firstRunException = await Record.ExceptionAsync(() => RunDueSubscriptions(factory));
        var subscriptionsAfterFailure = await GetSubscriptions(client);

        await RunDueSubscriptions(factory);

        var subscriptionsAfterRecovery = await GetSubscriptions(client);

        using var failureStory = new AssertionScope("subscription failure story");

        using (new AssertionScope("A subscription execution failure does not break the product flow"))
        {
            createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            firstRunException.Should().BeNull();
            subscriptionsAfterFailure.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            subscriptionsAfterFailure.Items.Should().ContainSingle(subscription =>
                subscription.Name == "Flaky subscription"
                && subscription.ItemsFound == null);
        }

        using (new AssertionScope("A later run can still succeed and show the user the result"))
        {
            executor.Executions.Should().HaveCount(2);
            subscriptionsAfterRecovery.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            subscriptionsAfterRecovery.Items.Should().ContainSingle(subscription =>
                subscription.Name == "Flaky subscription"
                && subscription.LastRun != null
                && subscription.ItemsFound == discoveredItems.Count);
        }
    }

    private static IReadOnlyList<SubscriptionDiscoveredItem> CreateDiscoveredItems()
    {
        return
        [
            CreateDiscoveredItem("https://fake-gallery.test/images/octans-1.jpg", "octans-1"),
            CreateDiscoveredItem("https://fake-gallery.test/images/octans-2.jpg", "octans-2")
        ];
    }

    private static SubscriptionDiscoveredItem CreateDiscoveredItem(string url, string name)
    {
        var bytes = TestingConstants.MinimalJpeg
            .Concat("\n"u8.ToArray())
            .Concat(JsonSerializer.SerializeToUtf8Bytes(name))
            .ToArray();
        var hash = ContentHash.FromContent(bytes);

        return new(new(url), bytes, hash);
    }

    private static async Task RunDueSubscriptions(OctansApiFactory factory)
    {
        await using var scope = factory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

        await service.CheckAndExecute();
    }

    private static async Task<SubscriptionResult> GetSubscriptions(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/subscriptions", UriKind.Relative));
        var subscriptions = response.StatusCode is HttpStatusCode.OK
            ? await TryReadJsonList<SubscriptionStatusDto>(response)
            : [];

        return new(response, subscriptions);
    }

    private static async Task<DownloadResult> GetDownloads(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/downloads", UriKind.Relative));
        var downloads = response.StatusCode is HttpStatusCode.OK
            ? await TryReadJsonList<DownloadStatusDto>(response)
            : [];

        return new(response, downloads);
    }

    private static async Task<ImportJobResult> GetImportJobs(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/import-jobs", UriKind.Relative));
        var jobs = response.StatusCode is HttpStatusCode.OK
            ? await TryReadJsonList<ImportJobDto>(response)
            : [];

        return new(response, jobs);
    }

    private static async Task<IReadOnlyList<T>> TryReadJsonList<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<List<T>>(OctansApiFactory.JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<MediaProbe>> ProbeMedia(
        HttpClient client,
        IReadOnlyList<SubscriptionDiscoveredItem> items)
    {
        var probes = new List<MediaProbe>();

        foreach (var item in items)
        {
            var detailsResponse = await client.GetAsync(new Uri($"/api/media/{item.Hash.Hex}/details", UriKind.Relative));
            var details = detailsResponse.StatusCode is HttpStatusCode.OK
                ? await detailsResponse.Content.ReadFromJsonAsync<MediaDetailsDto>(OctansApiFactory.JsonOptions)
                : null;
            var mediaResponse = await client.GetAsync(new Uri($"/media/{item.Hash.Hex}", UriKind.Relative));
            var mediaBytes = await mediaResponse.Content.ReadAsByteArrayAsync();

            probes.Add(new(item, detailsResponse, details, mediaResponse, mediaBytes));
        }

        return probes;
    }

    private static int CountSubscriptionDownloads(
        IReadOnlyList<DownloadStatusDto> downloads,
        IReadOnlyList<SubscriptionDiscoveredItem> discoveredItems)
    {
        return downloads.Count(download =>
            download.SourceType == "Subscription"
            && discoveredItems.Any(item => item.RemoteUrl.ToString() == download.Url));
    }

    private static int CountSubscriptionImportItems(
        IReadOnlyList<ImportJobDto> importJobs,
        IReadOnlyList<SubscriptionDiscoveredItem> discoveredItems)
    {
        return importJobs
            .SelectMany(job => job.Items)
            .Count(item => discoveredItems.Any(discovered => discovered.RemoteUrl.ToString() == item.Source));
    }

    private sealed class FakeSubscriptionExecutor(IReadOnlyList<SubscriptionDiscoveredItem> discoveredItems)
        : ISubscriptionExecutor
    {
        public bool FailNextExecution { get; set; }
        public List<ObservedSubscriptionExecution> Executions { get; } = [];

        public Task<SubscriptionExecutionResult> ExecuteAsync(
            Subscription subscription,
            CancellationToken cancellationToken)
        {
            Executions.Add(new(subscription.Name, subscription.Query, discoveredItems));

            if (FailNextExecution)
            {
                FailNextExecution = false;
                throw new InvalidOperationException("The fake subscription source failed.");
            }

            return Task.FromResult(new SubscriptionExecutionResult(discoveredItems.Count));
        }
    }

    private sealed record SubscriptionDiscoveredItem(Uri RemoteUrl, byte[] Bytes, ContentHash Hash);

    private sealed record ObservedSubscriptionExecution(
        string SubscriptionName,
        string Query,
        IReadOnlyList<SubscriptionDiscoveredItem> DiscoveredItems);

    private sealed record SubscriptionResult(
        HttpResponseMessage Response,
        IReadOnlyList<SubscriptionStatusDto> Items);

    private sealed record DownloadResult(HttpResponseMessage Response, IReadOnlyList<DownloadStatusDto> Items);

    private sealed record ImportJobResult(HttpResponseMessage Response, IReadOnlyList<ImportJobDto> Items);

    private sealed record MediaProbe(
        SubscriptionDiscoveredItem Item,
        HttpResponseMessage DetailsResponse,
        MediaDetailsDto? Details,
        HttpResponseMessage MediaResponse,
        byte[] MediaBytes);
}
