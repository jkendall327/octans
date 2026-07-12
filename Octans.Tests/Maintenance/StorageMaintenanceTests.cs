using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Core.Maintenance;
using Octans.Data.Models;
using Octans.Data.Models.Maintenance;
using Octans.Data.Models.Importing;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Maintenance;

public sealed class StorageMaintenanceTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly OctansTestHost _host;

    public StorageMaintenanceTests(DatabaseFixture databaseFixture, ITestOutputHelper output)
    {
        _host = OctansTestHost.Create(output, databaseFixture);
    }

    [Fact]
    public async Task QueueScanAsync_DeduplicatesActiveScansAndPersistsManualTrigger()
    {
        var service = _host.GetRequiredService<IStorageMaintenanceService>();

        var first = await service.QueueScanAsync();
        var second = await service.QueueScanAsync();

        second.JobId.Should().Be(first.JobId);
        var job = await service.GetJobAsync(first.JobId);
        job.Should().NotBeNull();
        job!.Status.Should().Be(StorageMaintenanceJobStatus.Queued);
        job.Trigger.Should().Be(StorageMaintenanceTrigger.Manual);
    }

    [Fact]
    public async Task Scan_DetectsMissingCorruptOrphanedAndMisplacedContent()
    {
        _host.EnsureImageStorage();
        var imageStorage = _host.GetRequiredService<ImageStorage>();
        var stored = await _host.AddStoredImageAsync(
            TestingConstants.MinimalJpeg,
            imageStorage.GetMetadata(TestingConstants.MinimalJpeg));
        _host.FileSystem.AddFile(stored.Path, new MockFileData("corrupt bytes"));

        var orphanBytes = "orphan"u8.ToArray();
        var orphanHash = ContentHash.FromContent(orphanBytes);
        var orphanPath = $"/app/db/files/{orphanHash.ContentBucket}/{orphanHash.Hex}.bin";
        _host.FileSystem.AddFile(orphanPath, new MockFileData(orphanBytes));

        var misplacedBytes = TestingConstants.MinimalJpeg.ToArray();
        var misplacedHash = ContentHash.FromContent(misplacedBytes);
        await using (var scope = _host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            db.Hashes.Add(new()
            {
                Hash = misplacedHash.Bytes,
                Extension = "jpeg",
                ContentType = "image/jpeg"
            });
            await db.SaveChangesAsync();
        }

        var misplacedPath = $"/app/db/files/f00/{misplacedHash.Hex}.jpeg";
        _host.FileSystem.AddFile(misplacedPath, new MockFileData(misplacedBytes));

        var service = _host.GetRequiredService<IStorageMaintenanceService>();
        var created = await service.QueueScanAsync();
        await _host.GetRequiredService<StorageMaintenanceProcessor>().ProcessNextAsync();

        var job = await service.GetJobAsync(created.JobId);
        var page = await service.GetFindingsAsync(created.JobId, take: 1000);

        job!.Status.Should().Be(StorageMaintenanceJobStatus.Completed);
        job.FindingsCount.Should().Be(page!.Total);
        page.Items.Select(f => f.Type).Should().Contain([
            StorageFindingType.ContentHashMismatch,
            StorageFindingType.MissingThumbnail,
            StorageFindingType.OrphanedOriginal,
            StorageFindingType.MisplacedOriginal
        ]);
    }

    [Fact]
    public async Task Repair_RegeneratesThumbnailsAndQuarantinesOrphansWithoutDeletingBytes()
    {
        _host.EnsureImageStorage();
        var imageStorage = _host.GetRequiredService<ImageStorage>();
        var stored = await _host.AddStoredImageAsync(
            TestingConstants.MinimalJpeg,
            imageStorage.GetMetadata(TestingConstants.MinimalJpeg));

        var orphanBytes = "preserve me"u8.ToArray();
        var orphanHash = ContentHash.FromContent(orphanBytes);
        var orphanPath = $"/app/db/files/{orphanHash.ContentBucket}/{orphanHash.Hex}.bin";
        _host.FileSystem.AddFile(orphanPath, new MockFileData(orphanBytes));

        var service = _host.GetRequiredService<IStorageMaintenanceService>();
        var scan = await service.QueueScanAsync();
        var processor = _host.GetRequiredService<StorageMaintenanceProcessor>();
        await processor.ProcessNextAsync();

        var repair = await service.QueueRepairAsync(scan.JobId, StorageRepairActions.AllSafe);
        await processor.ProcessNextAsync();

        var repairJob = await service.GetJobAsync(repair.JobId);
        var findings = await service.GetFindingsAsync(scan.JobId, take: 1000);
        var thumbnail = imageStorage.GetThumbnailDestination(stored.Hash);

        repairJob!.Status.Should().Be(StorageMaintenanceJobStatus.Completed);
        repairJob.RepairedItems.Should().BeGreaterThanOrEqualTo(2);
        _host.FileSystem.FileExists(thumbnail).Should().BeTrue();
        _host.FileSystem.FileExists(orphanPath).Should().BeFalse();

        var quarantinedPath = _host.FileSystem.AllFiles.Single(path => path.Contains("/quarantine/", StringComparison.Ordinal));
        (await _host.FileSystem.File.ReadAllBytesAsync(quarantinedPath)).Should().Equal(orphanBytes);
        findings!.Items.Where(f => f.Type == StorageFindingType.MissingThumbnail ||
                                   f.Type == StorageFindingType.OrphanedOriginal)
            .Should().OnlyContain(f => f.Resolution == StorageFindingResolution.Resolved);
    }

    [Fact]
    public async Task RepairMetadata_UsesDetectedTypeAndMovesOriginalToDeterministicPath()
    {
        _host.EnsureImageStorage();
        var bytes = TestingConstants.MinimalJpeg;
        var hash = ContentHash.FromContent(bytes);
        var wrongMetadata = new ImageMetadata("png", "image/png");
        var imageStorage = _host.GetRequiredService<ImageStorage>();
        var wrongPath = imageStorage.GetOriginalDestination(hash, wrongMetadata);
        _host.FileSystem.AddFile(wrongPath, new MockFileData(bytes));

        await using (var scope = _host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            db.Hashes.Add(new()
            {
                Hash = hash.Bytes,
                Extension = wrongMetadata.Extension,
                ContentType = wrongMetadata.ContentType
            });
            await db.SaveChangesAsync();
        }

        var service = _host.GetRequiredService<IStorageMaintenanceService>();
        var processor = _host.GetRequiredService<StorageMaintenanceProcessor>();
        var scan = await service.QueueScanAsync();
        await processor.ProcessNextAsync();
        await service.QueueRepairAsync(scan.JobId, StorageRepairActions.RepairMetadata);
        await processor.ProcessNextAsync();

        var detected = imageStorage.GetMetadata(bytes);
        var expectedPath = imageStorage.GetOriginalDestination(hash, detected);
        _host.FileSystem.FileExists(wrongPath).Should().BeFalse();
        _host.FileSystem.FileExists(expectedPath).Should().BeTrue();

        await using var assertionScope = _host.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var item = await assertionDb.Hashes.SingleAsync();
        item.Extension.Should().Be(detected.Extension);
        item.ContentType.Should().Be(detected.ContentType);
    }

    [Fact]
    public async Task RecoverInterruptedJobs_RequeuesScansAndDiscardsPartialFindings()
    {
        var now = _host.TimeProvider.GetUtcNow();
        var job = new StorageMaintenanceJob
        {
            Id = Guid.NewGuid(),
            Type = StorageMaintenanceJobType.Scan,
            Trigger = StorageMaintenanceTrigger.Automatic,
            Status = StorageMaintenanceJobStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
            ProcessedItems = 10,
            FindingsCount = 1
        };
        job.Findings.Add(new()
        {
            Id = Guid.NewGuid(),
            Message = "partial",
            Type = StorageFindingType.MissingOriginal
        });

        await using (var scope = _host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            db.StorageMaintenanceJobs.Add(job);
            await db.SaveChangesAsync();
        }

        await _host.GetRequiredService<StorageMaintenanceProcessor>().RecoverInterruptedJobsAsync();

        await using var assertionScope = _host.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var recovered = await assertionDb.StorageMaintenanceJobs.SingleAsync(j => j.Id == job.Id);
        recovered.Status.Should().Be(StorageMaintenanceJobStatus.Queued);
        recovered.ProcessedItems.Should().Be(0);
        (await assertionDb.StorageMaintenanceFindings.CountAsync(f => f.ScanJobId == job.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Coordinator_YieldsToUserWorkAndProcessesMaintenanceOnceIdle()
    {
        var service = _host.GetRequiredService<IStorageMaintenanceService>();
        var maintenance = await service.QueueScanAsync();
        var now = _host.TimeProvider.GetUtcNow();

        await using (var scope = _host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            db.ImportJobs.Add(new()
            {
                Id = Guid.NewGuid(),
                Status = ImportJobStatus.Running,
                SerializedRequest = "{}",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var coordinator = _host.GetRequiredService<StorageMaintenanceCoordinator>();
        (await coordinator.RunOnceIfIdleAsync()).Should().BeFalse();
        (await service.GetJobAsync(maintenance.JobId))!.Status.Should().Be(StorageMaintenanceJobStatus.Queued);

        await using (var scope = _host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            var import = await db.ImportJobs.SingleAsync();
            import.Status = ImportJobStatus.Completed;
            await db.SaveChangesAsync();
        }

        (await coordinator.RunOnceIfIdleAsync()).Should().BeTrue();
        (await service.GetJobAsync(maintenance.JobId))!.Status.Should().Be(StorageMaintenanceJobStatus.Completed);
    }

    public async Task InitializeAsync()
    {
        await _host.ResetDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }
}
