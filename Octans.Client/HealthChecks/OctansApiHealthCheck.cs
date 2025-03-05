using Microsoft.Extensions.Diagnostics.HealthChecks;
using Octans.Core.Communication;
using Refit;

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
        catch (ApiException apiEx)
        {
            // Handle specific API exceptions with status codes
            if (apiEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                return HealthCheckResult.Unhealthy($"API is unavailable (503): {apiEx.Message}");
            }
            
            return HealthCheckResult.Unhealthy($"API error: {apiEx.StatusCode}", apiEx);
        }
        catch (HttpRequestException httpEx)
        {
            // Handle network-level exceptions
            return HealthCheckResult.Unhealthy($"Network error: {httpEx.Message}", httpEx);
        }
        catch (Exception ex)
        {
            // Handle any other exceptions
            return HealthCheckResult.Unhealthy($"Unexpected error: {ex.Message}", ex);
        }
    }
}
