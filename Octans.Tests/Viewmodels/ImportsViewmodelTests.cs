using System.IO;
using System.IO.Abstractions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Core.Communication;
using Octans.Core.Importing;
using Refit;
using Xunit;

namespace Octans.Tests.Viewmodels;

// AI: Tests for LocalFileImportViewmodel
public class LocalFileImportViewmodelTests
{
    // AI: Shared dependencies and SUT
    private readonly IFileSystem _fs;
    private readonly IWebHostEnvironment _env;
    private readonly IOctansApi _api;
    private readonly LocalFileImportViewmodel _sut;

    public LocalFileImportViewmodelTests()
    {
        // AI: Substitute filesystem with in-memory streams and basic path/dir behavior
        _fs = Substitute.For<IFileSystem>();
        _env = Substitute.For<IWebHostEnvironment>();
        _api = Substitute.For<IOctansApi>();

        _env.WebRootPath.Returns("wwwroot");

        var path = Substitute.For<IPath>();

        path
            .Combine(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => System.IO.Path.Combine(ci.ArgAt<string>(0), ci.ArgAt<string>(1)));

        _fs.Path.Returns(path);

        var directory = Substitute.For<IDirectory>();
        _fs.Directory.Returns(directory);

        var fileStreamFactory = Substitute.For<IFileStreamFactory>();

        fileStreamFactory
            .New(Arg.Any<string>(), Arg.Any<FileMode>())
            .Returns(new MemoryStream());

        _fs.FileStream.Returns(fileStreamFactory);

        var apiResponse = Substitute.For<IApiResponse<ImportResult>>();

        _api
            .ProcessImport(Arg.Any<ImportRequest>())
            .Returns(Task.FromResult(apiResponse));

        _sut = new LocalFileImportViewmodel(_fs, _env, _api, NullLogger<LocalFileImportViewmodel>.Instance);
    }

    [Fact]
    public async Task SendLocalFilesToServer_sends_request_and_clears_files()
    {
        // AI: Arrange two non-empty files
        var file1 = Substitute.For<IBrowserFile>();
        file1.Name.Returns("a.jpg");
        file1.Size.Returns(3);

        file1
            .OpenReadStream(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(new byte[]
            {
                1, 2, 3
            }));

        var file2 = Substitute.For<IBrowserFile>();
        file2.Name.Returns("b.png");
        file2.Size.Returns(4);

        file2
            .OpenReadStream(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(new byte[]
            {
                4, 5, 6, 7
            }));

        _sut.LocalFiles = new List<IBrowserFile>
        {
            file1,
            file2
        };

        await _sut.SendLocalFilesToServer();

        await _api
            .Received(1)
            .ProcessImport(Arg.Is<ImportRequest>(r =>
                r.ImportType == ImportType.File && r.DeleteAfterImport == false && r.Items.Count == 2));

        Assert.Empty(_sut.LocalFiles);
    }

    [Fact]
    public async Task SendLocalFilesToServer_does_nothing_when_no_files()
    {
        _sut.LocalFiles = Array.Empty<IBrowserFile>();

        await _sut.SendLocalFilesToServer();

        await _api
            .DidNotReceive()
            .ProcessImport(Arg.Any<ImportRequest>());
    }
}

// AI: Tests for RawUrlImportViewmodel
public class RawUrlImportViewmodelTests
{
    // AI: Shared dependencies and SUT
    private readonly IOctansApi _api;
    private readonly RawUrlImportViewmodel _sut;

    public RawUrlImportViewmodelTests()
    {
        _api = Substitute.For<IOctansApi>();

        var apiResponse = Substitute.For<IApiResponse<ImportResult>>();

        _api
            .ProcessImport(Arg.Any<ImportRequest>())
            .Returns(Task.FromResult(apiResponse));

        _sut = new RawUrlImportViewmodel(_api, NullLogger<RawUrlImportViewmodel>.Instance);
    }

    [Fact]
    public async Task SendUrlsToServer_parses_lines_sends_request_and_clears_input()
    {
        _sut.RawInputs = "http://a\n\n  http://b  \r\n";
        _sut.AllowReimportDeleted = true;

        await _sut.SendUrlsToServer();

        await _api
            .Received(1)
            .ProcessImport(Arg.Is<ImportRequest>(r =>
                r.ImportType == ImportType.RawUrl && r.DeleteAfterImport == false && r.AllowReimportDeleted &&
                r.Items.Count == 2));

        Assert.Equal(string.Empty, _sut.RawInputs);
    }

    [Fact]
    public async Task SendUrlsToServer_does_nothing_when_input_empty()
    {
        _sut.RawInputs = "   \n  ";

        await _sut.SendUrlsToServer();

        await _api
            .DidNotReceive()
            .ProcessImport(Arg.Any<ImportRequest>());
    }
}