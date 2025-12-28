using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Client;
using Octans.Core.Models;
using Octans.Core.Progress;
using Octans.Core.Subscriptions;
using Xunit;

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
                return Task.FromResult(ctx);
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
                NextCheck = now.UtcDateTime.AddMinutes(-1) // Should be checked
            };
            context.Subscriptions.Add(subscription);
            await context.SaveChangesAsync();
        }

        _executor.ExecuteAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SubscriptionExecutionResult(42)));

        // Act
        await _sut.CheckAndExecute();

        // Assert
        using (var context = await _factory.CreateDbContextAsync())
        {
            var executions = await context.SubscriptionExecutions.ToListAsync();
            Assert.Single(executions);
            var execution = executions.First();
            Assert.Equal(42, execution.ItemsFound);
            Assert.Equal(now.UtcDateTime, execution.ExecutedAt);

            var subscription = await context.Subscriptions.FirstAsync();
            Assert.Equal(now.UtcDateTime.AddHours(1), subscription.NextCheck);
        }
    }
}
