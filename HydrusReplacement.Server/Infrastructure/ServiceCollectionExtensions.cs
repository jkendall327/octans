using System.IO.Abstractions;
using System.Threading.Channels;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HydrusReplacement.Server;

public static class ServiceCollectionExtensions
{
    public static void AddDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<ServerDbContext>((s, opt) =>
        {
            var path = s.GetRequiredService<IPath>();
    
            if (builder.Environment.IsDevelopment())
            {
                opt.UseInMemoryDatabase("db");
            }
            else
            {
                var dbFolder = path.Join(AppDomain.CurrentDomain.BaseDirectory, "db");

                var db = path.Join(dbFolder, "server.db");
        
                opt.UseSqlite($"Data Source={db};");
            }
        });
        
        builder.Services
            .AddHealthChecks()
            .AddDbContextCheck<ServerDbContext>("database", HealthStatus.Unhealthy);
    }

    public static void AddFilesystem(this WebApplicationBuilder builder)
    {
        var filesystem = new FileSystem();

        builder.Services.AddSingleton(filesystem.Path);
        builder.Services.AddSingleton(filesystem.DirectoryInfo);
        builder.Services.AddSingleton(filesystem.File);
    }

    public static void AddChannels(this WebApplicationBuilder builder)
    {
        var channel = Channel.CreateBounded<ThumbnailCreationRequest>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        builder.Services.AddSingleton(channel.Reader);
        builder.Services.AddSingleton(channel.Writer);
    }
    
    public static void AddBusinessServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<SubfolderManager>();
        builder.Services.AddScoped<FileFinder>();
        builder.Services.AddScoped<Importer>();
        builder.Services.AddScoped<ItemDeleter>();
    }
}