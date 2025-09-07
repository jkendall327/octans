using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;

namespace Octans.Tests;

/// <summary>
/// The actual value of these tests is checking if DI is set up correctly for the client project.
/// </summary>
public class ClientHealthcheckTest(WebApplicationFactory<Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task HealthcheckEndpointServesResponse()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health"));

        // The health check might return Healthy, Degraded, or Unhealthy
        // depending on the state of the API, but the endpoint itself should work
        var result = await response.Content.ReadAsStringAsync();

        helper.WriteLine($"Health check result: {result}");

        result
            .Should()
            .NotBeNullOrEmpty();
    }
}