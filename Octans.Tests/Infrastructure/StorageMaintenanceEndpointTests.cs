using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core.Maintenance;
using Octans.Data.Models.Maintenance;
using Xunit.Abstractions;

namespace Octans.Tests.Infrastructure;

public sealed class StorageMaintenanceEndpointTests(ITestOutputHelper output)
{
    [Fact]
    public async Task StorageMaintenanceApi_QueuesProcessesAndReturnsFindings()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/maintenance/storage/scans", UriKind.Relative), null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<StorageMaintenanceJobCreated>(OctansApiFactory.JsonOptions);

        await factory.Services.GetRequiredService<StorageMaintenanceProcessor>().ProcessNextAsync();

        var job = await client.GetFromJsonAsync<StorageMaintenanceJobDto>(
            new Uri($"/api/maintenance/storage/jobs/{created!.JobId}", UriKind.Relative),
            OctansApiFactory.JsonOptions);
        var findings = await client.GetFromJsonAsync<StorageMaintenanceFindingsPage>(
            new Uri($"/api/maintenance/storage/scans/{created.JobId}/findings", UriKind.Relative),
            OctansApiFactory.JsonOptions);

        job!.Status.Should().Be(StorageMaintenanceJobStatus.Completed);
        findings.Should().NotBeNull();
    }
}
