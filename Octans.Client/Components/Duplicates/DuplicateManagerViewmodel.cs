using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using Octans.Core.Duplicates;
using Octans.Core.Models;
using Octans.Core.Models.Duplicates;

namespace Octans.Client.Components.Duplicates;

public class DuplicateManagerViewmodel(
    DuplicateService duplicateService,
    ServerDbContext context,
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
            var candidates = await context.DuplicateCandidates
                .Include(c => c.Hash1)
                .Include(c => c.Hash2)
                .OrderByDescending(c => c.Distance)
                .Take(50)
                .ToListAsync();

            Candidates = candidates.Select(c => new DuplicateCandidateDto
            {
                Id = c.Id,
                HashId1 = c.HashId1,
                HashId2 = c.HashId2,
                Distance = c.Distance,
                // Url logic might need adjustment based on how we serve images
                Url1 = $"/api/files/{Convert.ToHexString(c.Hash1.Hash)}",
                Url2 = $"/api/files/{Convert.ToHexString(c.Hash2.Hash)}"
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
            await Task.Run(async () =>
            {
                await duplicateService.CalculateMissingHashes();
                await duplicateService.FindDuplicates();
            });
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

    public async Task Resolve(int candidateId, DuplicateResolution resolution, int? keepHashId)
    {
        try
        {
            await duplicateService.Resolve(candidateId, resolution, keepHashId);

            // Remove from local list
            var candidate = Candidates.FirstOrDefault(c => c.Id == candidateId);
            if (candidate != null)
            {
                Candidates.Remove(candidate);

                // If we deleted one, we should also remove any other candidates involving the deleted hash?
                // The service handles data integrity, but UI might show stale data.
                // We'll just reload or remove locally.
                if (keepHashId.HasValue)
                {
                     var deletedId = candidate.HashId1 == keepHashId ? candidate.HashId2 : candidate.HashId1;
                     Candidates.RemoveAll(c => c.HashId1 == deletedId || c.HashId2 == deletedId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve duplicate");
            snackbar.Add("Failed to resolve", Severity.Error);
        }
    }
}
