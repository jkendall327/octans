using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Octans.Core.Importing;
using Octans.Core.Importing.ImportFolders;
using Octans.Tests.Infrastructure;
using System.IO.Abstractions.TestingHelpers;

namespace Octans.Tests.Importing;

public sealed class ImportFolderScannerTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly SpyProgressReporter _progressReporter = new();
    private readonly IImportJobService _importJobService = Substitute.For<IImportJobService>();

    [Fact]
    public async Task ScanAndImportFolders_creates_file_import_job_for_images_in_configured_folders()
    {
        ImportJobCreateRequest? capturedRequest = null;
        _importJobService
            .Create(Arg.Do<ImportJobCreateRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImportJobCreatedDto(Guid.NewGuid())));

        _fileSystem.AddFile("/imports/a.JPG", new MockFileData("image"));
        _fileSystem.AddFile("/imports/not-image.txt", new MockFileData("text"));
        _fileSystem.AddFile("/imports/nested/b.png", new MockFileData("image"));

        var sut = CreateSut(["/imports"], deleteAfterImport: true);

        await sut.ScanAndImportFolders();

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ImportType.Should().Be(ImportType.File);
        capturedRequest.DeleteAfterImport.Should().BeTrue();
        capturedRequest.Sources.Should().BeEquivalentTo("/imports/a.JPG", "/imports/nested/b.png");
        capturedRequest.FilterData!.AllowedFileTypes.Should().BeEquivalentTo(".jpg", ".jpeg", ".png", ".gif");
        _progressReporter.Starts.Should().ContainSingle(s =>
            s.Operation == "Import folder scan" && s.TotalItems == 1);
        _progressReporter.Reports.Select(r => r.Processed).Should().Equal(1);
        _progressReporter.Completes.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanAndImportFolders_reports_missing_folders_and_imports_existing_images()
    {
        ImportJobCreateRequest? capturedRequest = null;
        _importJobService
            .Create(Arg.Do<ImportJobCreateRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImportJobCreatedDto(Guid.NewGuid())));

        _fileSystem.AddFile("/existing/a.gif", new MockFileData("image"));

        var sut = CreateSut(["/missing", "/existing"]);

        await sut.ScanAndImportFolders();

        capturedRequest!.Sources.Should().BeEquivalentTo("/existing/a.gif");
        _progressReporter.Starts.Should().ContainSingle(s =>
            s.Operation == "Import folder scan" && s.TotalItems == 2);
        _progressReporter.Reports.Select(r => r.Processed).Should().Equal(1, 2);
        _progressReporter.Completes.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanAndImportFolders_does_not_create_import_job_when_no_images_are_found()
    {
        _fileSystem.AddFile("/imports/not-image.txt", new MockFileData("text"));

        var sut = CreateSut(["/imports"]);

        await sut.ScanAndImportFolders();

        await _importJobService
            .DidNotReceive()
            .Create(Arg.Any<ImportJobCreateRequest>(), Arg.Any<CancellationToken>());
        _progressReporter.Starts.Should().ContainSingle(s =>
            s.Operation == "Import folder scan" && s.TotalItems == 1);
        _progressReporter.Reports.Select(r => r.Processed).Should().Equal(1);
        _progressReporter.Completes.Should().ContainSingle();
    }

    private ImportFolderScanner CreateSut(List<string> directories, bool deleteAfterImport = false) => new(
        Options.Create(new ImportFolderOptions
        {
            Directories = directories,
            DeleteAfterImport = deleteAfterImport
        }),
        _fileSystem,
        _progressReporter,
        _importJobService,
        NullLogger<ImportFolderScanner>.Instance);
}
