using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Core.Importing;

namespace Octans.Tests.Viewmodels;

public class RawUrlImportViewmodelTests
{
    private readonly IImporter _importer;
    private readonly RawUrlImportViewmodel _sut;

    public RawUrlImportViewmodelTests()
    {
        _importer = Substitute.For<IImporter>();

        var importResult = new ImportResult(Guid.NewGuid(), []);

        _importer
            .ProcessImport(Arg.Any<ImportRequest>())
            .Returns(Task.FromResult(importResult));

        _sut = new(_importer, NullLogger<RawUrlImportViewmodel>.Instance);
    }

    [Fact]
    public async Task SendUrlsToServer_parses_lines_sends_request_and_clears_input()
    {
        _sut.RawInputs = "http://a\n\n  http://b  \r\n";
        _sut.AllowReimportDeleted = true;

        await _sut.SendUrlsToServer();

        await _importer
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

        await _importer
            .DidNotReceive()
            .ProcessImport(Arg.Any<ImportRequest>());
    }
}