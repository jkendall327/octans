using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Octans.Core.Downloads;
using Octans.Core.Downloads.Bandwidth;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;
using Octans.Tests.Helpers;

namespace Octans.Tests.Downloads;

public sealed class DownloadManagerIntegrationTests
{
    [Fact]
    public async Task DownloadManager_ProcessesQueuedDownloadThroughRealServices()
    {
        await using var harness = await DownloadManagerHarness.Create();
        harness.HttpHandler.AddResponse("https://cdn.example/files/a.txt", "alpha");

        var downloadService = harness.Services.GetRequiredService<IDownloadService>();
        var downloadId = await downloadService.QueueDownloadAsync(new()
        {
            Url = new("https://cdn.example/files/a.txt"),
            DestinationPath = "/downloads/a.txt",
            DisplayName = "Alpha",
            SourceType = "Subscription",
            SourceId = "sub-1",
            Priority = 10
        });

        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Notifier.CompletedDownloads.Any(d => d.Id == downloadId));

        var stateService = harness.Services.GetRequiredService<IDownloadStateService>();
        var status = stateService.GetDownloadById(downloadId);

        Assert.NotNull(status);
        Assert.Equal(DownloadState.Completed, status.State);
        Assert.Equal(5, status.BytesDownloaded);
        Assert.Equal(5, status.TotalBytes);
        Assert.Equal("Alpha", status.DisplayName);
        Assert.Equal("Subscription", status.SourceType);
        Assert.Equal("sub-1", status.SourceId);
        Assert.Equal("alpha", await harness.FileSystem.File.ReadAllTextAsync("/downloads/a.txt"));

        var queue = harness.Services.GetRequiredService<IDownloadQueue>();
        Assert.Equal(0, await queue.GetQueuedCountAsync());

        await using var db = await harness.CreateDbContextAsync();
        var savedStatus = await db.DownloadStatuses.FindAsync(downloadId);
        Assert.NotNull(savedStatus);
        Assert.Equal(DownloadState.Completed, savedStatus.State);
        Assert.Equal(5, savedStatus.BytesDownloaded);
    }

    [Fact]
    public async Task DownloadManager_RestoresInterruptedDownloadAndRemovesStaleStagingFile()
    {
        await using var harness = await DownloadManagerHarness.Create();
        var downloadId = Guid.NewGuid();
        var url = new Uri("https://cdn.example/files/restarted.txt");
        var destinationPath = "/downloads/restarted.txt";
        var stagingPath = GetStagingPath(harness.FileSystem, downloadId, destinationPath);

        harness.HttpHandler.PauseBeforeResponding = true;
        harness.HttpHandler.AddResponse(url.ToString(), "fresh bytes");
        harness.FileSystem.Directory.CreateDirectory(harness.FileSystem.Path.GetDirectoryName(stagingPath)!);
        await harness.FileSystem.File.WriteAllTextAsync(stagingPath, "stale partial");

        await using (var db = await harness.CreateDbContextAsync())
        {
            db.DownloadStatuses.Add(new()
            {
                Id = downloadId,
                Url = url.ToString(),
                Filename = "restarted.txt",
                DestinationPath = destinationPath,
                State = DownloadState.InProgress,
                CreatedAt = TestClock.UtcNow,
                StartedAt = TestClock.UtcNow,
                LastUpdated = TestClock.UtcNow,
                Domain = url.Host
            });
            await db.SaveChangesAsync();
        }

        await harness.StartAsync();

        await WaitUntilAsync(() => harness.HttpHandler.StartedRequests.Contains(url));

        Assert.False(harness.FileSystem.File.Exists(stagingPath));

        harness.HttpHandler.ReleaseResponses();

        await WaitUntilAsync(() => harness.Notifier.CompletedDownloads.Any(d => d.Id == downloadId));

        Assert.Equal("fresh bytes", await harness.FileSystem.File.ReadAllTextAsync(destinationPath));
    }

    [Fact]
    public async Task DownloadManager_EnforcesPerDomainConcurrencyWhileDrainingQueue()
    {
        await using var harness = await DownloadManagerHarness.Create(options =>
        {
            options.MaxConcurrentDownloads = 2;
            options.MaxConcurrentDownloadsPerDomain = 1;
        });

        harness.HttpHandler.PauseBeforeResponding = true;
        harness.HttpHandler.AddResponse("https://same.example/files/one.txt", "one");
        harness.HttpHandler.AddResponse("https://same.example/files/two.txt", "two");
        harness.HttpHandler.AddResponse("https://other.example/files/three.txt", "three");

        var downloadService = harness.Services.GetRequiredService<IDownloadService>();
        var firstSame = await Queue(downloadService, "https://same.example/files/one.txt", "/downloads/one.txt");
        var secondSame = await Queue(downloadService, "https://same.example/files/two.txt", "/downloads/two.txt");
        var other = await Queue(downloadService, "https://other.example/files/three.txt", "/downloads/three.txt");

        await harness.StartAsync();

        await WaitUntilAsync(() => harness.HttpHandler.ActiveRequestCount == 2);

        Assert.Contains(new Uri("https://same.example/files/one.txt"), harness.HttpHandler.StartedRequests);
        Assert.Contains(new Uri("https://other.example/files/three.txt"), harness.HttpHandler.StartedRequests);
        Assert.DoesNotContain(new Uri("https://same.example/files/two.txt"), harness.HttpHandler.StartedRequests);
        Assert.Equal(1, harness.HttpHandler.MaxActiveRequestsForHost("same.example"));

        harness.HttpHandler.ReleaseResponses();

        await WaitUntilAsync(
            () => harness.Notifier.CompletedDownloads.Length == 3,
            async () => $"completed={harness.Notifier.CompletedDownloads.Length}, " +
                        $"started={string.Join(", ", harness.HttpHandler.StartedRequests.Select(u => u.ToString()))}, " +
                        $"states={DescribeStates(harness.Services, firstSame, secondSame, other)}, " +
                        $"queued={await harness.Services.GetRequiredService<IDownloadQueue>().GetQueuedCountAsync()}");

        var stateService = harness.Services.GetRequiredService<IDownloadStateService>();
        Assert.Equal(DownloadState.Completed, stateService.GetDownloadById(firstSame)?.State);
        Assert.Equal(DownloadState.Completed, stateService.GetDownloadById(secondSame)?.State);
        Assert.Equal(DownloadState.Completed, stateService.GetDownloadById(other)?.State);
        Assert.Equal(1, harness.HttpHandler.MaxActiveRequestsForHost("same.example"));
        Assert.Equal("one", await harness.FileSystem.File.ReadAllTextAsync("/downloads/one.txt"));
        Assert.Equal("two", await harness.FileSystem.File.ReadAllTextAsync("/downloads/two.txt"));
        Assert.Equal("three", await harness.FileSystem.File.ReadAllTextAsync("/downloads/three.txt"));
    }

    [Fact]
    public async Task DownloadManager_PacesStreamThroughBandwidthGate()
    {
        await using var harness = await DownloadManagerHarness.Create(
            configureBandwidth: options => options.DefaultBytesPerSecond = 100);

        var body = new string('x', 250);
        var url = new Uri("https://slow.example/files/large.txt");
        harness.HttpHandler.AddResponse(url.ToString(), body);

        var downloadService = harness.Services.GetRequiredService<IDownloadService>();
        var downloadId = await Queue(downloadService, url.ToString(), "/downloads/large.txt");

        await harness.StartAsync();

        await WaitUntilAsync(() => harness.HttpHandler.StartedRequests.Contains(url));

        Assert.DoesNotContain(harness.Notifier.CompletedDownloads, d => d.Id == downloadId);

        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.DoesNotContain(harness.Notifier.CompletedDownloads, d => d.Id == downloadId);

        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));

        await WaitUntilAsync(() => harness.Notifier.CompletedDownloads.Any(d => d.Id == downloadId));

        Assert.Equal(body, await harness.FileSystem.File.ReadAllTextAsync("/downloads/large.txt"));
    }

    private static async Task<Guid> Queue(IDownloadService downloadService, string url, string destinationPath)
    {
        return await downloadService.QueueDownloadAsync(new()
        {
            Url = new(url),
            DestinationPath = destinationPath
        });
    }

    private static string GetStagingPath(MockFileSystem fileSystem, Guid downloadId, string destinationPath)
    {
        var destinationDirectory = fileSystem.Path.GetDirectoryName(destinationPath) ??
                                   throw new InvalidOperationException();

        return fileSystem.Path.Combine(destinationDirectory, ".octans-downloads", $"{downloadId}.part");
    }

    private static string DescribeStates(IServiceProvider services, params Guid[] ids)
    {
        var stateService = services.GetRequiredService<IDownloadStateService>();
        return string.Join(", ", ids.Select(id =>
        {
            var status = stateService.GetDownloadById(id);
            return status is null ? $"{id}:missing" : $"{id}:{status.State}:{status.ErrorMessage}";
        }));
    }

    private static Task WaitUntilAsync(Func<bool> condition)
    {
        return WaitUntilAsync(condition, () => Task.FromResult("Condition was not met before the timeout."));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<Task<string>> describeFailure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(await describeFailure());
        }
    }

    private sealed class DownloadManagerHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private DownloadManagerHarness(
            SqliteConnection connection,
            ServiceProvider services,
            MockFileSystem fileSystem,
            IntegrationHttpMessageHandler httpHandler,
            TrackingCompletionNotifier notifier)
        {
            _connection = connection;
            Services = services;
            FileSystem = fileSystem;
            HttpHandler = httpHandler;
            Notifier = notifier;
        }

        public ServiceProvider Services { get; }
        public MockFileSystem FileSystem { get; }
        public IntegrationHttpMessageHandler HttpHandler { get; }
        public TrackingCompletionNotifier Notifier { get; }
        public required FakeTimeProvider TimeProvider { get; init; }

        public static async Task<DownloadManagerHarness> Create(
            Action<DownloadManagerOptions>? configure = null,
            Action<BandwidthLimiterOptions>? configureBandwidth = null)
        {
            var connectionString = $"Data Source=DownloadManagerIntegrationTests-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var fileSystem = new MockFileSystem();
            var httpHandler = new IntegrationHttpMessageHandler();
            var notifier = new TrackingCompletionNotifier();
            var timeProvider = new FakeTimeProvider(TestClock.UtcNow);

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddDebug());
            services.AddSingleton<TimeProvider>(timeProvider);
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IDownloadCompletionNotifier>(notifier);
            services.AddDbContextFactory<ServerDbContext>(options => options.UseSqlite(connectionString));
            services.AddBandwidthLimiter(configureBandwidth);
            services.AddDownloadManager(configure);
            services.AddHttpClient("DownloadClient")
                .ConfigurePrimaryHttpMessageHandler(() => httpHandler);

            var provider = services.BuildServiceProvider();
            await using (var db = await provider.GetRequiredService<IDbContextFactory<ServerDbContext>>()
                             .CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            return new(connection, provider, fileSystem, httpHandler, notifier)
            {
                TimeProvider = timeProvider
            };
        }

        public async Task StartAsync()
        {
            await DownloadWorker.StartAsync(CancellationToken.None);
        }

        public async Task<ServerDbContext> CreateDbContextAsync()
        {
            return await Services.GetRequiredService<IDbContextFactory<ServerDbContext>>().CreateDbContextAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DownloadWorker.StopAsync(CancellationToken.None);
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private DownloadBackgroundService DownloadWorker =>
            Services.GetServices<IHostedService>().OfType<DownloadBackgroundService>().Single();
    }

    private sealed class TrackingCompletionNotifier : IDownloadCompletionNotifier
    {
        private readonly ConcurrentQueue<DownloadStatus> _completedDownloads = new();

        public DownloadStatus[] CompletedDownloads => _completedDownloads.ToArray();

        public Task DownloadCompletedAsync(DownloadStatus status, CancellationToken cancellationToken = default)
        {
            _completedDownloads.Enqueue(status);
            return Task.CompletedTask;
        }
    }

    private sealed class IntegrationHttpMessageHandler : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<Uri, byte[]> _responses = new();
        private readonly Dictionary<string, int> _activeRequestsByHost = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _maxActiveRequestsByHost = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Uri> _startedRequests = [];
        private readonly TaskCompletionSource _releaseResponses =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool PauseBeforeResponding { get; set; }

        public int ActiveRequestCount
        {
            get
            {
                lock (_lock)
                {
                    return _activeRequestCount;
                }
            }
        }

        private int _activeRequestCount;

        public IReadOnlyCollection<Uri> StartedRequests
        {
            get
            {
                lock (_lock)
                {
                    return _startedRequests.ToArray();
                }
            }
        }

        public void AddResponse(string url, string body)
        {
            _responses[new(url)] = System.Text.Encoding.UTF8.GetBytes(body);
        }

        public int MaxActiveRequestsForHost(string host)
        {
            lock (_lock)
            {
                return _maxActiveRequestsByHost.GetValueOrDefault(host);
            }
        }

        public void ReleaseResponses()
        {
            _releaseResponses.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            TrackRequestStarted(requestUri);

            try
            {
                if (PauseBeforeResponding)
                {
                    await _releaseResponses.Task.WaitAsync(cancellationToken);
                }

                if (!_responses.TryGetValue(requestUri, out var body))
                {
                    return new(HttpStatusCode.NotFound);
                }

                return new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body)
                };
            }
            finally
            {
                TrackRequestFinished(requestUri.Host);
            }
        }

        private void TrackRequestStarted(Uri requestUri)
        {
            lock (_lock)
            {
                _activeRequestCount++;
                _startedRequests.Add(requestUri);
                _activeRequestsByHost.TryGetValue(requestUri.Host, out var hostCount);
                hostCount++;
                _activeRequestsByHost[requestUri.Host] = hostCount;
                _maxActiveRequestsByHost.TryGetValue(requestUri.Host, out var maxHostCount);
                _maxActiveRequestsByHost[requestUri.Host] = Math.Max(maxHostCount, hostCount);
            }
        }

        private void TrackRequestFinished(string host)
        {
            lock (_lock)
            {
                _activeRequestCount--;
                _activeRequestsByHost.TryGetValue(host, out var hostCount);
                if (hostCount <= 1)
                {
                    _activeRequestsByHost.Remove(host);
                    return;
                }

                _activeRequestsByHost[host] = hostCount - 1;
            }
        }
    }
}
