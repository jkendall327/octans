using Octans.Core.Maintenance;
using Octans.Data.Models.Maintenance;

namespace Octans.Client.Components.Maintenance;

public sealed class StorageMaintenanceViewmodel(IOctansClient client) : ViewmodelBase
{
    public IReadOnlyList<StorageMaintenanceJobDto> Jobs { get; private set; } = [];
    public IReadOnlyList<StorageMaintenanceFindingDto> Findings { get; private set; } = [];
    public StorageMaintenanceJobDto? SelectedScan { get; private set; }
    public int TotalFindings { get; private set; }
    public string? Error { get; private set; }
    public bool IsBusy { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Jobs = await client.GetStorageMaintenanceJobsAsync(cancellationToken);
            var selectedId = SelectedScan?.Id;
            SelectedScan = Jobs.FirstOrDefault(j => j.Id == selectedId && j.Type is StorageMaintenanceJobType.Scan)
                           ?? Jobs.FirstOrDefault(j => j.Type is StorageMaintenanceJobType.Scan);
            await LoadFindings(cancellationToken);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        await NotifyStateChanged();
    }

    public async Task SelectScanAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        SelectedScan = Jobs.FirstOrDefault(j => j.Id == scanId && j.Type is StorageMaintenanceJobType.Scan);
        await LoadFindings(cancellationToken);
        await NotifyStateChanged();
    }

    public async Task QueueScanAsync(CancellationToken cancellationToken = default)
    {
        await Run(async () =>
        {
            var created = await client.QueueStorageScanAsync(cancellationToken);
            await RefreshAsync(cancellationToken);
            SelectedScan = Jobs.FirstOrDefault(j => j.Id == created.JobId) ?? SelectedScan;
        });
    }

    public async Task QueueRepairAsync(StorageRepairActions actions, CancellationToken cancellationToken = default)
    {
        if (SelectedScan is null)
        {
            return;
        }

        await Run(async () =>
        {
            await client.QueueStorageRepairAsync(SelectedScan.Id, actions, cancellationToken);
            await RefreshAsync(cancellationToken);
        });
    }

    public async Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await Run(async () =>
        {
            await client.CancelStorageMaintenanceJobAsync(jobId, cancellationToken);
            await RefreshAsync(cancellationToken);
        });
    }

    private async Task LoadFindings(CancellationToken cancellationToken)
    {
        if (SelectedScan is null)
        {
            Findings = [];
            TotalFindings = 0;
            return;
        }

        var page = await client.GetStorageMaintenanceFindingsAsync(
            SelectedScan.Id,
            take: 1000,
            cancellationToken: cancellationToken);
        Findings = page?.Items ?? [];
        TotalFindings = page?.Total ?? 0;
    }

    private async Task Run(Func<Task> action)
    {
        IsBusy = true;
        Error = null;
        await NotifyStateChanged();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await NotifyStateChanged();
        }
    }
}
