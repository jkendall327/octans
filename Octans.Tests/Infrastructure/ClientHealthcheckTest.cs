using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NSubstitute;
using Octans.Client.HealthChecks;
using Octans.Core.Communication;
using Refit;
using System.Net;
using Xunit.Abstractions;

namespace Octans.Tests;

public class ClientHealthcheckTest : IClassFixture<WebApplicationFactory<Octans.Client.Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Octans.Client.Program> _factory;
    private readonly ITestOutputHelper _helper;

    public ClientHealthcheckTest(WebApplicationFactory<Octans.Client.Program> factory, ITestOutputHelper helper)
    {
        _factory = factory;
        
        _helper = helper;
    }

    [Fact]
    public async Task HealthcheckEndpointServesResponse()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health"));

        // The health check might return Healthy, Degraded, or Unhealthy
        // depending on the state of the API, but the endpoint itself should work
        var result = await response.Content.ReadAsStringAsync();
        
        _helper.WriteLine($"Health check result: {result}");
        
        result.Should().NotBeNullOrEmpty();
    }
    
    [Fact]
    public async Task HealthCheck_HandlesServiceUnavailable()
    {
        // Arrange
        var mockApi = Substitute.For<IOctansApi>();
        var apiException = await ApiException.Create(
            new(), 
            HttpMethod.Get, 
            new(HttpStatusCode.ServiceUnavailable), new());
            
        mockApi.HealthCheck().Returns(Task.FromException<IApiResponse>(apiException));
        
        var healthCheck = new OctansApiHealthCheck(mockApi);
        
        // Act
        var result = await healthCheck.CheckHealthAsync(new(), CancellationToken.None);
        
        // Assert
        result.Status.Should().Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);
        result.Description.Should().Contain("503");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;
}
