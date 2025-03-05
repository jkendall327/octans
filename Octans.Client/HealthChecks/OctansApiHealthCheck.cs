using Microsoft.Extensions.Diagnostics.HealthChecks;
using Octans.Core.Communication;

namespace Octans.Client.HealthChecks;

public class OctansApiHealthCheck : IHealthCheck
{
    private readonly IOctansApi _octansApi;

    public OctansApiHealthCheck(IOctansApi octansApi)
    {
        _octansApi = octansApi;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _octansApi.HealthCheck();
            
            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("API is healthy");
            }
            
            return HealthCheckResult.Degraded($"API returned status code {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("API health check failed", ex);
        }
    }
}
