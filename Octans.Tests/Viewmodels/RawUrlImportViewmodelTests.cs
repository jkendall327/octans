using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Importing;
using Octans.Core.Importing;

namespace Octans.Tests.Viewmodels;

public class RawUrlImportViewmodelTests
{
    private readonly IOctansClient _client;
    private readonly RawUrlImportViewmodel _sut;

    public RawUrlImportViewmodelTests()
    {
        _client = Substitute.For<IOctansClient>();

        var created = new ImportJobClientResult(Guid.NewGuid(), "/import-jobs/test");

        _client
            .CreateImportJobAsync(Arg.Any<ImportJobCreateRequest>())
            .Returns(Task.FromResult(created));

        _sut = new(_client, NullLogger<RawUrlImportViewmodel>.Instance);
    }

    [Fact]
    public async Task SendUrlsToServer_parses_lines_sends_request_and_clears_input()
    {
        _sut.RawInputs = "http://a\n\n  http://b  \r\n";
        _sut.AllowReimportDeleted = true;

        await _sut.SendUrlsToServer();

        await _client
            .Received(1)
            .CreateImportJobAsync(Arg.Is<ImportJobCreateRequest>(r =>
                r.ImportType == ImportType.RawUrl && r.DeleteAfterImport == false && r.AllowReimportDeleted &&
                r.Sources.Count == 2));

        Assert.Equal(string.Empty, _sut.RawInputs);
    }

    [Fact]
    public async Task SendUrlsToServer_does_nothing_when_input_empty()
    {
        _sut.RawInputs = "   \n  ";

        await _sut.SendUrlsToServer();

        await _client
            .DidNotReceive()
            .CreateImportJobAsync(Arg.Any<ImportJobCreateRequest>());
    }
}
