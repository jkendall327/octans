using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Octans.Client;
using Octans.Core;
using Xunit.Abstractions;

namespace Octans.Tests.Infrastructure;

public sealed class OctansApiFactoryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Health_ShouldReturnHealthy()
    {
        using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("Healthy");
    }

    [Fact]
    public async Task Version_ShouldReturnApiVersion()
    {
        using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();

        var version = await client.GetFromJsonAsync<OctansVersion>(new Uri("/version", UriKind.Relative));

        version.Should().Be(new OctansVersion("1.0.0"));
    }

    [Fact]
    public void Factory_ShouldDisableHostedServicesByDefault()
    {
        using var factory = new OctansApiFactory(output);
        _ = factory.CreateClient();

        factory.Services
            .GetServices<IHostedService>()
            .Should()
            .NotContain(service => service.GetType().Assembly == typeof(OctansCoreServiceOptions).Assembly);
    }
}
