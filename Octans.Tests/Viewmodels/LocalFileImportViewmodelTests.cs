using System.IO.Abstractions.TestingHelpers;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Importing;
using Octans.Core.Importing;

namespace Octans.Tests.Viewmodels;

public class LocalFileImportViewmodelTests
{
    private readonly IOctansClient _client;
    private readonly LocalFileImportViewmodel _sut;

    public LocalFileImportViewmodelTests()
    {
        MockFileSystem fs = new();
        var env = Substitute.For<IWebHostEnvironment>();
        _client = Substitute.For<IOctansClient>();
        _client
            .CreateImportJobAsync(Arg.Any<ImportJobCreateRequest>())
            .Returns(Task.FromResult(new ImportJobClientResult(Guid.NewGuid(), "/import-jobs/test")));

        // This has to be a root path to avoid the URI ctor breaking.
        env.WebRootPath.Returns("/wwwroot");

        _sut = new(fs, env, _client, NullLogger<LocalFileImportViewmodel>.Instance);
    }

    [Fact]
    public async Task SendLocalFilesToServer_sends_request_and_clears_files()
    {
        var file1 = Substitute.For<IBrowserFile>();
        file1.Name.Returns("a.jpg");
        file1.Size.Returns(3);

        file1
            .OpenReadStream()
            .Returns(new MemoryStream([
                1, 2, 3
            ]));

        var file2 = Substitute.For<IBrowserFile>();
        file2.Name.Returns("b.png");
        file2.Size.Returns(4);

        file2
            .OpenReadStream()
            .Returns(new MemoryStream([
                4, 5, 6, 7
            ]));

        _sut.LocalFiles = new List<IBrowserFile>
        {
            file1,
            file2
        };

        await _sut.SendLocalFilesToServer();

        await _client
            .Received(1)
            .CreateImportJobAsync(Arg.Is<ImportJobCreateRequest>(r =>
                r.ImportType == ImportType.File && r.DeleteAfterImport == false && r.Sources.Count == 2));

        Assert.Empty(_sut.LocalFiles);
    }

    [Fact]
    public async Task SendLocalFilesToServer_does_nothing_when_no_files()
    {
        _sut.LocalFiles = [];

        await _sut.SendLocalFilesToServer();

        await _client
            .DidNotReceive()
            .CreateImportJobAsync(Arg.Any<ImportJobCreateRequest>());
    }
}
