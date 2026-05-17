using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Core.Filesystem;
using Octans.Core.Http;
using Octans.Core.Http.Models;
using Octans.Core.Importing;
using Octans.Core.Importing.RawByteProviders;
using Octans.Data.Models;

namespace Octans.Tests.Importing;

public sealed class SimpleImporterTests
{
    private readonly IDownloadService _downloadService = Substitute.For<IDownloadService>();
    private readonly MockFileSystem _fileSystem = new();
    private readonly SimpleImporter _sut;

    public SimpleImporterTests()
    {
        var fileWriter = new RobustFileWriter(_fileSystem, NullLogger<RobustFileWriter>.Instance);
        _sut = new(_downloadService, fileWriter, _fileSystem, NullLogger<SimpleImporter>.Instance);
    }

    [Fact]
    public async Task GetRawBytes_DownloadsThroughDownloadManagerAndReadsTempFile()
    {
        var uri = new Uri("https://example.com/images/sample.jpg");
        var expectedBytes = "downloaded"u8.ToArray();
        string? destination = null;

        _downloadService
            .QueueDownloadAndWaitAsync(Arg.Any<DownloadRequest>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var request = call.ArgAt<DownloadRequest>(0);
                destination = request.DestinationPath;
                _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(destination)!);
                await _fileSystem.File.WriteAllBytesAsync(destination, expectedBytes);

                return new DownloadJobResult
                {
                    DownloadId = Guid.NewGuid(),
                    Outcome = DownloadTerminalOutcome.Completed,
                    Url = request.Url.ToString(),
                    DestinationPath = request.DestinationPath
                };
            });

        var bytes = await _sut.GetRawBytes(new()
        {
            Url = uri
        });

        bytes.Should().BeEquivalentTo(expectedBytes);
        destination.Should().NotBeNull();
        _fileSystem.File.Exists(destination!).Should().BeFalse("temporary raw URL downloads should be removed");
        await _downloadService
            .Received(1)
            .QueueDownloadAndWaitAsync(
                Arg.Is<DownloadRequest>(request =>
                    request.Url == uri &&
                    request.SourceType == nameof(ImportType.RawUrl) &&
                    request.SourceId == uri.ToString() &&
                    request.DestinationPath.EndsWith(".jpg", StringComparison.Ordinal)),
                null,
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRawBytes_WhenDownloadFails_ThrowsFailureMessageAndDeletesTempFile()
    {
        var uri = new Uri("https://example.com/images/missing.jpg");
        string? destination = null;

        _downloadService
            .QueueDownloadAndWaitAsync(Arg.Any<DownloadRequest>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var request = call.ArgAt<DownloadRequest>(0);
                destination = request.DestinationPath;
                _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(destination)!);
                await _fileSystem.File.WriteAllTextAsync(destination, "partial");

                return new DownloadJobResult
                {
                    DownloadId = Guid.NewGuid(),
                    Outcome = DownloadTerminalOutcome.TerminalHttpFailure,
                    Url = request.Url.ToString(),
                    DestinationPath = request.DestinationPath,
                    ErrorMessage = "HTTP 404"
                };
            });

        var action = () => _sut.GetRawBytes(new()
        {
            Url = uri
        });

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*TerminalHttpFailure*HTTP 404*");
        destination.Should().NotBeNull();
        _fileSystem.File.Exists(destination!).Should().BeFalse("temporary files are only an implementation detail");
    }
}
