using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
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
    public async Task ClientIsHealthyOnStartup()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health"));

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();

        result.Should().Be("Healthy");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;
}
