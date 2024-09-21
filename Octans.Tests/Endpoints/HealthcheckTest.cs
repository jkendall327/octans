using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;

namespace Octans.Tests;

public class HealthcheckTest(WebApplicationFactory<Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task AppIsHealthyOnStartup()
    {
        var client = factory.CreateClient();
        
        var response = await client.GetAsync("/health");
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadAsStringAsync();
        
        result.Should().Be("Healthy");
    }
}