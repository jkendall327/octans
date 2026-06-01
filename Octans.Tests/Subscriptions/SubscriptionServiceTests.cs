using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Core.Progress;
using Octans.Core.Subscriptions;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Subscriptions;

namespace Octans.Tests.Subscriptions;

public class SubscriptionServiceTests
{
    private readonly IDbContextFactory<ServerDbContext> _factory;
    private readonly FakeTimeProvider _timeProvider;
    private readonly IBackgroundProgressReporter _reporter;
    private readonly ISubscriptionExecutor _executor;
    private readonly SubscriptionService _sut;

    public SubscriptionServiceTests()
    {
        // Use in-memory database for testing
        var options = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        // Setup the database context factory
        // We need to keep the connection open for in-memory sqlite
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();

        var contextOptions = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new ServerDbContext(contextOptions);
        context.Database.EnsureCreated();

        _factory = Substitute.For<IDbContextFactory<ServerDbContext>>();
        _factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ctx = new ServerDbContext(contextOptions);
#pragma warning disable CA2025
                return Task.FromResult(ctx);
#pragma warning restore CA2025
            });

        _timeProvider = new FakeTimeProvider();
        _reporter = Substitute.For<IBackgroundProgressReporter>();
        _executor = Substitute.For<ISubscriptionExecutor>();

        _sut = new SubscriptionService(
            _factory,
            _timeProvider,
            _reporter,
            _executor,
            new NullLogger<SubscriptionService>());
    }

    [Fact]
    public async Task CheckAndExecute_PersistsExecutionResult()
    {
        // Arrange
        var now = new DateTimeOffset(2023, 10, 1, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await using (var context = await _factory.CreateDbContextAsync())
        {
            var provider = new Provider { Name = "TestProvider" };
            context.Providers.Add(provider);
            await context.SaveChangesAsync();

            var subscription = new Subscription
            {
                Name = "Test Subscription",
                CheckPeriod = TimeSpan.FromHours(1),
                Query = "test query",
                ProviderId = provider.Id,
                NextCheck = now.AddMinutes(-1) // Should be checked
            };
            context.Subscriptions.Add(subscription);
            await context.SaveChangesAsync();
        }

        var discoveredItems = new List<SubscriptionDiscoveredItem>
        {
            new("source-1", new Uri("https://subscriptions.test/source-1.jpg")),
            new("source-2", new Uri("https://subscriptions.test/source-2.jpg"))
        };
        _executor.ExecuteAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SubscriptionExecutionResult(discoveredItems)));

        // Act
        await _sut.CheckAndExecute();

        // Assert
        await using (var context = await _factory.CreateDbContextAsync())
        {
            var executions = await context.SubscriptionExecutions.ToListAsync();
            Assert.Single(executions);
            var execution = executions.First();
            Assert.Equal(SubscriptionExecutionStatus.Succeeded, execution.Status);
            Assert.Equal(2, execution.ItemsFound);
            Assert.Equal(now, execution.ExecutedAt);

            var subscription = await context.Subscriptions.FirstAsync();
            Assert.Equal(now.AddHours(1), subscription.NextCheck);
        }
    }

    [Fact]
    public async Task AddAsync_PersistsImportSettingsAndTags()
    {
        // Arrange
        var tags = new List<TagModel>
        {
            new("series", "octans subscription"),
            new("source", "fake-gallery-downloader")
        };

        // Act
        await _sut.AddAsync(
            "Runnable subscription",
            "TestProvider",
            "artist:octans",
            TimeSpan.FromMinutes(30),
            new(RepositoryType.Archive, AllowReimportDeleted: true, AutoArchive: true),
            tags);

        // Assert
        await using var context = await _factory.CreateDbContextAsync();
        var subscription = await context.Subscriptions.SingleAsync();
        var persistedTags = JsonSerializer.Deserialize<List<TagModel>>(subscription.SerializedTags!);

        Assert.Equal((int)RepositoryType.Archive, subscription.RepositoryId);
        Assert.True(subscription.AllowReimportDeleted);
        Assert.True(subscription.AutoArchive);
        Assert.Equal(tags, persistedTags);
    }

    [Fact]
    public async Task CheckAndExecute_RecordsFailureAndContinuesRunningDueSubscriptions()
    {
        // Arrange
        var now = new DateTimeOffset(2023, 10, 1, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await using (var context = await _factory.CreateDbContextAsync())
        {
            var provider = new Provider { Name = "TestProvider" };
            context.Providers.Add(provider);
            await context.SaveChangesAsync();

            context.Subscriptions.AddRange(
                new Subscription
                {
                    Name = "Flaky Subscription",
                    CheckPeriod = TimeSpan.FromHours(1),
                    Query = "flaky query",
                    ProviderId = provider.Id,
                    NextCheck = now.AddMinutes(-1)
                },
                new Subscription
                {
                    Name = "Healthy Subscription",
                    CheckPeriod = TimeSpan.FromHours(2),
                    Query = "healthy query",
                    ProviderId = provider.Id,
                    NextCheck = now.AddMinutes(-1)
                });
            await context.SaveChangesAsync();
        }

        _executor.ExecuteAsync(Arg.Is<Subscription>(s => s.Name == "Flaky Subscription"), Arg.Any<CancellationToken>())
            .Returns<SubscriptionExecutionResult>(_ => throw new InvalidOperationException("Subscription source failed."));
        _executor.ExecuteAsync(Arg.Is<Subscription>(s => s.Name == "Healthy Subscription"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SubscriptionExecutionResult(12)));

        // Act
        await _sut.CheckAndExecute();

        // Assert
        await using (var context = await _factory.CreateDbContextAsync())
        {
            var executions = await context.SubscriptionExecutions
                .Include(e => e.Subscription)
                .OrderBy(e => e.Subscription.Name)
                .ToListAsync();

            Assert.Equal(2, executions.Count);

            var flakyExecution = executions[0];
            Assert.Equal("Flaky Subscription", flakyExecution.Subscription.Name);
            Assert.Equal(SubscriptionExecutionStatus.Failed, flakyExecution.Status);
            Assert.Null(flakyExecution.ItemsFound);
            Assert.Equal("Subscription source failed.", flakyExecution.ErrorMessage);

            var healthyExecution = executions[1];
            Assert.Equal("Healthy Subscription", healthyExecution.Subscription.Name);
            Assert.Equal(SubscriptionExecutionStatus.Succeeded, healthyExecution.Status);
            Assert.Equal(12, healthyExecution.ItemsFound);

            var subscriptions = await context.Subscriptions.OrderBy(s => s.Name).ToListAsync();
            Assert.Equal(now.AddHours(1), subscriptions[0].NextCheck);
            Assert.Equal(now.AddHours(2), subscriptions[1].NextCheck);
        }
    }
}
