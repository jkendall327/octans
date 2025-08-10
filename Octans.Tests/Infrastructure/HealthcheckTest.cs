using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;

namespace Octans.Tests;

public class HealthcheckTest(WebApplicationFactory<Client.Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task AppIsHealthyOnStartup()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health"));

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();

        result.Should().Be("Healthy");
    }
}