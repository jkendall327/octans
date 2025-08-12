using System.IO.Abstractions.TestingHelpers;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Core.Communication;
using Octans.Core.Importing;
using Refit;

namespace Octans.Tests.Viewmodels;

public class LocalFileImportViewmodelTests
{
    private readonly IOctansApi _api;
    private readonly LocalFileImportViewmodel _sut;

    public LocalFileImportViewmodelTests()
    {
        MockFileSystem fs = new();
        var env = Substitute.For<IWebHostEnvironment>();
        _api = Substitute.For<IOctansApi>();

        // This has to be a root path to avoid the URI ctor breaking.
        env.WebRootPath.Returns("/wwwroot");

        var apiResponse = Substitute.For<IApiResponse<ImportResult>>();

        _api
            .ProcessImport(Arg.Any<ImportRequest>())
            .Returns(Task.FromResult(apiResponse));

        _sut = new(fs, env, _api, NullLogger<LocalFileImportViewmodel>.Instance);
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

        await _api
            .Received(1)
            .ProcessImport(Arg.Is<ImportRequest>(r =>
                r.ImportType == ImportType.File && r.DeleteAfterImport == false && r.Items.Count == 2));

        Assert.Empty(_sut.LocalFiles);
    }

    [Fact]
    public async Task SendLocalFilesToServer_does_nothing_when_no_files()
    {
        _sut.LocalFiles = [];

        await _sut.SendLocalFilesToServer();

        await _api
            .DidNotReceive()
            .ProcessImport(Arg.Any<ImportRequest>());
    }
}