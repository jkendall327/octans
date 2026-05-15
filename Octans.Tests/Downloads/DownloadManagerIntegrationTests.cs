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

        await WaitUntilAsync(() => harness.Notifier.FinishedDownloads.Any(d => d.DownloadId == downloadId));

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

        var result = await downloadService.GetResultAsync(downloadId);
        Assert.NotNull(result);
        Assert.Equal(DownloadTerminalOutcome.Completed, result.Outcome);
        Assert.Equal(downloadId, result.DownloadId);
        Assert.Equal("Alpha", result.DisplayName);
        Assert.Equal("Subscription", result.SourceType);
        Assert.Equal("sub-1", result.SourceId);
        Assert.Equal(5, result.BytesDownloaded);
        Assert.Equal(200, result.HttpStatusCode);

        var queue = harness.Services.GetRequiredService<IDownloadQueue>();
        Assert.Equal(0, await queue.GetQueuedCountAsync());

        await using var db = await harness.CreateDbContextAsync();
        var savedStatus = await db.DownloadStatuses.FindAsync(downloadId);
        Assert.NotNull(savedStatus);
        Assert.Equal(DownloadState.Completed, savedStatus.State);
        Assert.Equal(5, savedStatus.BytesDownloaded);
    }

    [Fact]
    public async Task DownloadManager_NotifiesTerminalHttpFailureResult()
    {
        await using var harness = await DownloadManagerHarness.Create();

        var downloadService = harness.Services.GetRequiredService<IDownloadService>();
        var downloadId = await downloadService.QueueDownloadAsync(new()
        {
            Url = new("https://cdn.example/files/missing.txt"),
            DestinationPath = "/downloads/missing.txt",
            SourceType = "Subscription",
            SourceId = "sub-missing"
        });

        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Notifier.FinishedDownloads.Any(d => d.DownloadId == downloadId));

        var notifiedResult = harness.Notifier.FinishedDownloads.Single(d => d.DownloadId == downloadId);
        Assert.Equal(DownloadTerminalOutcome.TerminalHttpFailure, notifiedResult.Outcome);
        Assert.Equal(DownloadFailureCategory.Http, notifiedResult.FailureCategory);
        Assert.Equal(404, notifiedResult.HttpStatusCode);
        Assert.Equal("Subscription", notifiedResult.SourceType);
        Assert.Equal("sub-missing", notifiedResult.SourceId);

        var persistedResult = await downloadService.GetResultAsync(downloadId);
        Assert.NotNull(persistedResult);
        Assert.Equal(DownloadTerminalOutcome.TerminalHttpFailure, persistedResult.Outcome);
        Assert.Equal(404, persistedResult.HttpStatusCode);
        Assert.False(harness.FileSystem.File.Exists("/downloads/missing.txt"));
    }

    [Fact]
    public async Task DownloadManager_RetriesTransientFailureWithoutRetryingTerminalFailure()
    {
        await using var harness = await DownloadManagerHarness.Create(options =>
        {
            options.HostCircuitBreaker.RetryDelay = TimeSpan.Zero;
            options.HostCircuitBreaker.MaxRetryAttempts = 2;
        });

        var transientUrl = new Uri("https://cdn.example/files/eventual.txt");
        var terminalUrl = new Uri("https://cdn.example/files/terminal.txt");
        harness.HttpHandler.AddResponseSequence(
            transientUrl.ToString(),
            () => new(HttpStatusCode.InternalServerError),
            () => new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("eventual"))
            });
        harness.HttpHandler.AddResponse(terminalUrl.ToString(), HttpStatusCode.NotFound);

        var downloadService = harness.Services.GetRequiredService<IDownloadService>();
        var transient = await Queue(downloadService, transientUrl.ToString(), "/downloads/eventual.txt");
        var terminal = await Queue(downloadService, terminalUrl.ToString(), "/downloads/terminal.txt");

        await harness.StartAsync();

        await WaitUntilAsync(
            () => harness.Notifier.FinishedDownloads.Length == 2,
            () => Task.FromResult($"completed={harness.Notifier.FinishedDownloads.Length}, " +
                                  $"states={DescribeStates(harness.Services, transient, terminal)}"));

        var stateService = harness.Services.GetRequiredService<IDownloadStateService>();
        Assert.Equal(DownloadState.Completed, stateService.GetDownloadById(transient)?.State);
        Assert.Equal(DownloadState.Failed, stateService.GetDownloadById(terminal)?.State);
        Assert.Equal(2, harness.HttpHandler.RequestCountFor(transientUrl));
        Assert.Equal(1, harness.HttpHandler.RequestCountFor(terminalUrl));
        Assert.Equal("eventual", await harness.FileSystem.File.ReadAllTextAsync("/downloads/eventual.txt"));
        Assert.False(harness.FileSystem.File.Exists("/downloads/terminal.txt"));
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

        await WaitUntilAsync(() => harness.Notifier.FinishedDownloads.Any(d => d.DownloadId == downloadId));

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
            () => harness.Notifier.FinishedDownloads.Length == 3,
            async () => $"completed={harness.Notifier.FinishedDownloads.Length}, " +
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
    public async Task DownloadManager_SkipsOpenHostCircuitAndContinuesHealthyHosts()
    {
        await using var harness = await DownloadManagerHarness.Create(options =>
        {
            options.MaxConcurrentDownloads = 1;
            options.HostCircuitBreaker.BreakDuration = TimeSpan.FromSeconds(5);
        });

        harness.HttpHandler.AddResponse("https://bad.example/files/deferred.txt", "deferred");
        harness.HttpHandler.AddResponse("https://healthy.example/files/ready.txt", "ready");
        harness.HostCircuitRegistry.OpenCircuit("bad.example", TimeSpan.FromSeconds(5));

        var downloadService = harness.Services.GetRequiredService<IDownloadService>();
        var deferred = await Queue(downloadService, "https://bad.example/files/deferred.txt", "/downloads/deferred.txt");
        var ready = await Queue(downloadService, "https://healthy.example/files/ready.txt", "/downloads/ready.txt");

        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Notifier.FinishedDownloads.Any(d => d.DownloadId == ready));

        Assert.DoesNotContain(new Uri("https://bad.example/files/deferred.txt"), harness.HttpHandler.StartedRequests);
        Assert.Equal(1, await harness.Services.GetRequiredService<IDownloadQueue>().GetQueuedCountAsync());

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(6));

        await WaitUntilAsync(
            () => harness.Notifier.FinishedDownloads.Any(d => d.DownloadId == deferred),
            async () => $"completed={harness.Notifier.FinishedDownloads.Length}, " +
                        $"started={string.Join(", ", harness.HttpHandler.StartedRequests.Select(u => u.ToString()))}, " +
                        $"states={DescribeStates(harness.Services, deferred, ready)}, " +
                        $"queued={await harness.Services.GetRequiredService<IDownloadQueue>().GetQueuedCountAsync()}");

        var stateService = harness.Services.GetRequiredService<IDownloadStateService>();
        Assert.Equal(DownloadState.Completed, stateService.GetDownloadById(deferred)?.State);
        Assert.Equal(DownloadState.Completed, stateService.GetDownloadById(ready)?.State);
        Assert.Equal("deferred", await harness.FileSystem.File.ReadAllTextAsync("/downloads/deferred.txt"));
        Assert.Equal("ready", await harness.FileSystem.File.ReadAllTextAsync("/downloads/ready.txt"));
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

        Assert.DoesNotContain(harness.Notifier.FinishedDownloads, d => d.DownloadId == downloadId);

        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.DoesNotContain(harness.Notifier.FinishedDownloads, d => d.DownloadId == downloadId);

        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));

        await WaitUntilAsync(() => harness.Notifier.FinishedDownloads.Any(d => d.DownloadId == downloadId));

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
        public IDownloadHostCircuitRegistry HostCircuitRegistry =>
            Services.GetRequiredService<IDownloadHostCircuitRegistry>();
        public required FakeTimeProvider TimeProvider { get; init; }

        public static async Task<DownloadManagerHarness> Create(
            Action<DownloadManagerOptions>? configure = null,
            Action<BandwidthLimiterOptions>? configureBandwidth = null)
        {
            var connectionString = $"Data Source=DownloadManagerIntegrationTests-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var fileSystem = new MockFileSystem();
            fileSystem.AddDrive("/", new()
            {
                AvailableFreeSpace = 1024 * 1024 * 1024,
                TotalFreeSpace = 1024 * 1024 * 1024,
                TotalSize = 1024L * 1024 * 1024 * 10
            });
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
        private readonly ConcurrentQueue<DownloadJobResult> _finishedDownloads = new();

        public DownloadJobResult[] FinishedDownloads => _finishedDownloads.ToArray();

        public Task DownloadFinishedAsync(DownloadJobResult result, CancellationToken cancellationToken = default)
        {
            _finishedDownloads.Enqueue(result);
            return Task.CompletedTask;
        }
    }

    private sealed class IntegrationHttpMessageHandler : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<Uri, byte[]> _responses = new();
        private readonly Dictionary<Uri, Queue<Func<HttpResponseMessage>>> _responseSequences = new();
        private readonly Dictionary<Uri, int> _requestCounts = new();
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

        public void AddResponse(string url, HttpStatusCode statusCode)
        {
            AddResponseSequence(url, () => new(statusCode));
        }

        public void AddResponseSequence(string url, params Func<HttpResponseMessage>[] responses)
        {
            _responseSequences[new(url)] = new(responses);
        }

        public int MaxActiveRequestsForHost(string host)
        {
            lock (_lock)
            {
                return _maxActiveRequestsByHost.GetValueOrDefault(host);
            }
        }

        public int RequestCountFor(Uri uri)
        {
            lock (_lock)
            {
                return _requestCounts.GetValueOrDefault(uri);
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
                    if (_responseSequences.TryGetValue(requestUri, out var sequence) && sequence.TryDequeue(out var next))
                    {
                        return next();
                    }

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
                _requestCounts.TryGetValue(requestUri, out var requestCount);
                _requestCounts[requestUri] = requestCount + 1;
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
