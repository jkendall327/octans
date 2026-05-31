using MudBlazor;
using Octans.Data.Models.Duplicates;

namespace Octans.Client.Components.Duplicates;

public class DuplicateManagerViewmodel(
    IOctansClient octansClient,
    ISnackbar snackbar,
    ILogger<DuplicateManagerViewmodel> logger)
{
    public List<DuplicateCandidateDto> Candidates { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public bool IsCalculating { get; private set; }

    public async Task Initialize()
    {
        await LoadCandidates();
    }

    public async Task LoadCandidates()
    {
        IsLoading = true;
        try
        {
            var candidates = await octansClient.GetDuplicateCandidatesAsync();

            Candidates = candidates
                .OrderByDescending(c => c.Distance)
                .Take(50)
                .Select(c => new DuplicateCandidateDto
            {
                Id = c.Id,
                HashId1 = c.HashId1,
                HashId2 = c.HashId2,
                Distance = c.Distance,
                Url1 = c.MediaUrl1,
                Url2 = c.MediaUrl2
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load candidates");
            snackbar.Add("Failed to load candidates", Severity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task TriggerCheck()
    {
        IsCalculating = true;
        try
        {
            await octansClient.ScanDuplicatesAsync();
            await LoadCandidates();
            snackbar.Add("Check complete", Severity.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run duplicate check");
            snackbar.Add("Failed to run duplicate check", Severity.Error);
        }
        finally
        {
            IsCalculating = false;
        }
    }

    public async Task Resolve(int candidateId, DuplicateCandidateResolution resolution, int? keepHashId)
    {
        try
        {
            await octansClient.ResolveDuplicateCandidateAsync(candidateId, MapResolution(resolution), keepHashId);
            await LoadCandidates();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve duplicate");
            snackbar.Add("Failed to resolve", Severity.Error);
        }
    }

    private static DuplicateResolution MapResolution(DuplicateCandidateResolution resolution) =>
        resolution switch
        {
            DuplicateCandidateResolution.Distinct => DuplicateResolution.Distinct,
            DuplicateCandidateResolution.KeepBoth => DuplicateResolution.KeepBoth,
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown duplicate resolution.")
        };
}
