using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Octans.Core.Importing;
using Octans.Data.Models;
using Octans.Data.Models.Importing;
using Octans.Tests.Helpers;

namespace Octans.Tests.Importing;

public sealed class ImportJobServiceTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _databaseFixture;
    private readonly IServiceProvider _provider;
    private readonly FakeTimeProvider _timeProvider = new(new(2026, 5, 14, 12, 0, 0, TimeSpan.Zero));

    public ImportJobServiceTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        var services = new ServiceCollection();

        services.AddDbContext<ServerDbContext>(options => { options.UseSqlite(databaseFixture.Connection); },
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<ServerDbContext>();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton<IImportJobNotifier, NoOpImportJobNotifier>();
        services.AddScoped<ImportJobService>();
        services.AddScoped<IImportJobService>(s => s.GetRequiredService<ImportJobService>());
        services.AddSingleton<ILogger<ImportJobService>>(NullLogger<ImportJobService>.Instance);

        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Create_file_job_persists_pending_items()
    {
        var sut = _provider.GetRequiredService<IImportJobService>();

        var created = await sut.Create(new()
        {
            ImportType = Octans.Core.Importing.ImportType.File,
            Sources = ["/imports/a.jpg", "/imports/b.png"],
            DeleteAfterImport = true,
            AutoArchive = true
        });

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var job = await db.ImportJobs.Include(j => j.Items).SingleAsync(j => j.Id == created.JobId);

        job.Status.Should().Be(ImportJobStatus.Queued);
        job.TotalItems.Should().Be(2);
        job.DeleteAfterImport.Should().BeTrue();
        job.AutoArchive.Should().BeTrue();
        job.CreatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        job.Items.Should().HaveCount(2);
        job.Items.Should().OnlyContain(i => i.Status == ImportItemStatus.Pending);
        job.Items.Should().OnlyContain(i => i.ImportType == Octans.Data.Models.Importing.ImportType.File);
        job.Items.Select(i => i.Source).Should().BeEquivalentTo("/imports/a.jpg", "/imports/b.png");
    }

    [Fact]
    public async Task Pause_resume_and_cancel_apply_durable_transitions()
    {
        var sut = _provider.GetRequiredService<IImportJobService>();
        var created = await sut.Create(new()
        {
            ImportType = Octans.Core.Importing.ImportType.RawUrl,
            Sources = ["https://example.test/a.jpg"]
        });

        var paused = await sut.PauseJob(created.JobId);

        paused!.Status.Should().Be(nameof(ImportJobStatus.Paused));

        var resumed = await sut.ResumeJob(created.JobId);

        resumed!.Status.Should().Be(nameof(ImportJobStatus.Queued));

        var cancelled = await sut.CancelJob(created.JobId);

        cancelled!.Status.Should().Be(nameof(ImportJobStatus.Cancelled));
        cancelled.Items.Should().OnlyContain(i => i.Status == nameof(ImportItemStatus.Cancelled));
    }

    public async Task InitializeAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
