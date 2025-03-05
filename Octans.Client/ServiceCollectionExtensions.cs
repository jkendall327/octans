using System.IO.Abstractions;
using Octans.Client.Components.Pages;
using Octans.Core;
using Octans.Core.Communication;
using Octans.Core.Infrastructure;
using Refit;

namespace Octans.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOctansServices(this IServiceCollection services)
    {
        services.AddScoped<SubfolderManager>();
        services.AddSingleton<ImportRequestSender>();
        services.AddHostedService<ImportFolderBackgroundService>();

        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.AddSingleton<StorageService>();
        return services;
    }

    public static IServiceCollection AddHttpClients(this IServiceCollection services)
    {
        services.AddRefitClient<IOctansApi>().ConfigureHttpClient(client =>
        {
            var port = CommunicationConstants.OCTANS_SERVER_PORT;
            client.BaseAddress = new($"http://localhost:{port}/");
        });

        return services;
    }

    public static IServiceCollection AddViewmodels(this IServiceCollection services)
    {
        services.AddScoped<GalleryViewmodel>();
        services.AddScoped<ImportsViewmodel>();
        return services;
    }

    public static void SetupConfiguration(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection(nameof(GlobalSettings));
        builder.Services.Configure<GlobalSettings>(config);
    }
}