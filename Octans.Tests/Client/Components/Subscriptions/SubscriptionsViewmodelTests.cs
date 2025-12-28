using System.IO.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using NSubstitute;
using Octans.Client.Components.Subscriptions;
using Octans.Core.Downloaders;
using Octans.Core.Models;
using Octans.Core.Progress;
using Octans.Core.Subscriptions;
using Octans.Client;
using Octans.Core;
using Xunit;
using Microsoft.Extensions.Options;

namespace Octans.Tests.Client.Components.Subscriptions;

public class SubscriptionsViewmodelTests
{
    private readonly SubscriptionService _subscriptionService;
    private readonly DownloaderFactory _downloaderFactory;
    private readonly IDialogService _dialogService;
    private readonly SubscriptionsViewmodel _sut;
    private readonly ServerDbContext _dbContext;

    public SubscriptionsViewmodelTests()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(connection)
            .Options;
        _dbContext = new ServerDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Create a new context for each factory call to simulate scope
        var factory = Substitute.For<IDbContextFactory<ServerDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var ctx = new ServerDbContext(options);
            return ctx;
        });
        factory.CreateDbContextAsync().Returns(_ =>
        {
            var ctx = new ServerDbContext(options);
            return ctx;
        });

        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        timeProvider.SetUtcNow(DateTime.UtcNow);

        var reporter = Substitute.For<IBackgroundProgressReporter>();
        var executor = Substitute.For<ISubscriptionExecutor>();
        var logger = NullLogger<SubscriptionService>.Instance;

        _subscriptionService = new SubscriptionService(factory, timeProvider, reporter, executor, logger);

        var fileSystem = Substitute.For<IFileSystem>();
        var settings = Options.Create(new GlobalSettings { AppRoot = "/" });
        var dlLogger = NullLogger<DownloaderFactory>.Instance;
        _downloaderFactory = Substitute.For<DownloaderFactory>(fileSystem, settings, dlLogger);

        _dialogService = Substitute.For<IDialogService>();

        _sut = new SubscriptionsViewmodel(_subscriptionService, _downloaderFactory, _dialogService);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadSubscriptions()
    {
        // Arrange
        var provider = new Provider { Name = "TestDownloader" };
        _dbContext.Providers.Add(provider);
        _dbContext.Subscriptions.Add(new Subscription
        {
            Name = "TestSub",
            Provider = provider,
            Query = "TestQuery",
            CheckPeriod = TimeSpan.FromMinutes(60),
            NextCheck = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.InitializeAsync();

        // Assert
        _sut.Subscriptions.Should().HaveCount(1);
        _sut.Subscriptions[0].Name.Should().Be("TestSub");
        _sut.Subscriptions[0].DownloaderName.Should().Be("TestDownloader");
    }

    [Fact]
    public async Task AddSubscriptionAsync_ShouldAddSubscription_WhenDialogConfirmed()
    {
        // Arrange
        // Mock DownloaderFactory to return a downloader
        var lua = new NLua.Lua();
        lua.DoString("function match_url(url) return true end function classify_url(url) return 'Post' end function parse_html(html) return {} end");
        var functions = new Dictionary<string, NLua.Lua>
        {
            { "classifier", lua },
            { "parser", lua }
        };

        _downloaderFactory.GetDownloaders().Returns(Task.FromResult(new List<Downloader>
        {
            new Downloader(functions, new DownloaderMetadata { Name = "TestDownloader" })
        }));

        // Mock DialogService to return result
        var dialogReference = Substitute.For<IDialogReference>();
        var formModel = new AddSubscriptionDialog.FormModel
        {
            Name = "NewSub",
            Downloader = "TestDownloader",
            Query = "NewQuery",
            FrequencyMinutes = 30
        };
        var dialogResult = DialogResult.Ok(formModel);
        dialogReference.Result.Returns(Task.FromResult<DialogResult?>(dialogResult));
        _dialogService.ShowAsync<AddSubscriptionDialog>(Arg.Any<string>(), Arg.Any<DialogParameters<AddSubscriptionDialog>>())
            .Returns(Task.FromResult(dialogReference));

        // Act
        await _sut.AddSubscriptionAsync();

        // Assert
        _dbContext.Subscriptions.Should().ContainSingle(s => s.Name == "NewSub" && s.Query == "NewQuery");
        _sut.Subscriptions.Should().ContainSingle(s => s.Name == "NewSub");
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_ShouldRemoveSubscription()
    {
         // Arrange
        var provider = new Provider { Name = "TestDownloader" };
        _dbContext.Providers.Add(provider);
        var sub = new Subscription
        {
            Name = "TestSub",
            Provider = provider,
            Query = "TestQuery",
            CheckPeriod = TimeSpan.FromMinutes(60),
            NextCheck = DateTime.UtcNow
        };
        _dbContext.Subscriptions.Add(sub);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteSubscriptionAsync(sub.Id);

        // Assert
        _dbContext.Subscriptions.Should().BeEmpty();
        _sut.Subscriptions.Should().BeEmpty();
    }
}
