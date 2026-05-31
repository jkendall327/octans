using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Duplicates;
using Octans.Data.Models.Duplicates;

using ApiDuplicateCandidateDto = Octans.Client.DuplicateCandidateDto;

namespace Octans.Tests.Viewmodels;

public sealed class DuplicateManagerViewmodelTests
{
    private readonly IOctansClient _octansClient = Substitute.For<IOctansClient>();
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
    private readonly DuplicateManagerViewmodel _sut;

    public DuplicateManagerViewmodelTests()
    {
        _octansClient
            .GetDuplicateCandidatesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ApiDuplicateCandidateDto>>([]));

        _sut = new(_octansClient, _snackbar, NullLogger<DuplicateManagerViewmodel>.Instance);
    }

    [Fact]
    public async Task Initialize_LoadsCandidatesFromApiClient()
    {
        _octansClient
            .GetDuplicateCandidatesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ApiDuplicateCandidateDto>>([
                new(1, 11, "AAAA", "/media/AAAA", 12, "BBBB", "/media/BBBB", 3),
                new(2, 21, "CCCC", "/media/CCCC", 22, "DDDD", "/media/DDDD", 8)
            ]));

        await _sut.Initialize();

        Assert.False(_sut.IsLoading);
        Assert.Equal([2, 1], _sut.Candidates.Select(c => c.Id));
        Assert.Equal("/media/CCCC", _sut.Candidates[0].Url1);
        Assert.Equal("/media/DDDD", _sut.Candidates[0].Url2);
    }

    [Fact]
    public async Task TriggerCheck_ScansThenReloadsCandidates()
    {
        _octansClient
            .ScanDuplicatesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DuplicateScanResultDto(2, 1)));

        _octansClient
            .GetDuplicateCandidatesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ApiDuplicateCandidateDto>>([
                new(1, 11, "AAAA", "/media/AAAA", 12, "BBBB", "/media/BBBB", 3)
            ]));

        await _sut.TriggerCheck();

        Assert.False(_sut.IsCalculating);
        await _octansClient
            .Received(1)
            .ScanDuplicatesAsync(Arg.Any<CancellationToken>());
        Assert.Single(_sut.Candidates);
    }

    [Fact]
    public async Task Resolve_SendsApiResolutionAndReloads()
    {
        _octansClient
            .GetDuplicateCandidatesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ApiDuplicateCandidateDto>>([
                    new(5, 11, "AAAA", "/media/AAAA", 12, "BBBB", "/media/BBBB", 3)
                ]),
                Task.FromResult<IReadOnlyList<ApiDuplicateCandidateDto>>([]));

        await _sut.Initialize();

        await _sut.Resolve(5, DuplicateCandidateResolution.KeepBoth, 11);

        await _octansClient
            .Received(1)
            .ResolveDuplicateCandidateAsync(
                5,
                DuplicateResolution.KeepBoth,
                11,
                Arg.Any<CancellationToken>());
        Assert.Empty(_sut.Candidates);
    }
}
