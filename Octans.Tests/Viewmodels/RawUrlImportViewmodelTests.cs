using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Core.Communication;
using Octans.Core.Importing;
using Refit;

namespace Octans.Tests.Viewmodels;

public class RawUrlImportViewmodelTests
{
    private readonly IOctansApi _api;
    private readonly RawUrlImportViewmodel _sut;

    public RawUrlImportViewmodelTests()
    {
        _api = Substitute.For<IOctansApi>();

        var apiResponse = Substitute.For<IApiResponse<ImportResult>>();

        _api
            .ProcessImport(Arg.Any<ImportRequest>())
            .Returns(Task.FromResult(apiResponse));

        _sut = new(_api, NullLogger<RawUrlImportViewmodel>.Instance);
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