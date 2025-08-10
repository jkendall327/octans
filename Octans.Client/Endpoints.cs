using Microsoft.AspNetCore.Mvc;
using Octans.Core;
using Octans.Core.Downloaders;
using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Core.Querying;
using Octans.Core.Tags;
using Octans.Server.Services;

namespace Octans.Server;

internal static class Endpoints
{
    public static void AddEndpoints(this WebApplication app)
    {
        MapFileEndpoints(app);

        MapTagEndpoints(app);

        MapDownloaderEndpoints(app);

        MapInfrastructureEndpoints(app);
    }

    private static void MapTagEndpoints(WebApplication app)
    {
        app
            .MapPost("/tags",
                async ([FromBody] UpdateTagsRequest request, [FromServices] TagUpdater updater) =>
                {
                    var success = await updater.UpdateTags(request);

                    return success ? Results.Ok() : Results.BadRequest();
                })
            .WithName("UpdateTags")
            .WithDescription("Add and remove tags for a specific image")
            .WithOpenApi();
    }

    private static void MapDownloaderEndpoints(WebApplication app)
    {
        app.MapGet("/downloaders",
            async ([FromServices] DownloaderFactory ds) =>
            {
                var downloaders = await ds.GetDownloaders();

                return downloaders.Select(d => d.Metadata);
            });

        app.MapGet("/downloaders/{name}",
            async (string name, [FromServices] DownloaderFactory ds) =>
            {
                var downloaders = await ds.GetDownloaders();

                var downloader = downloaders.SingleOrDefault(s => s.Metadata.Name == name);

                return downloader;
            });
    }

    private static void MapFileEndpoints(WebApplication app)
    {
        app.MapGet("/files", async ([FromServices] FileFinder service) => await service.GetAll());

        app
            .MapGet("/files/{id:int}",
                async (int id, [FromServices] FileFinder service) =>
                {
                    var file = await service.GetFile(id);

                    return file is null ? Results.NotFound() : Results.Ok(file);
                })
            .WithDescription("Get a single file by its ID")
            .WithOpenApi();

        app
            .MapPost("/files/query",
                async ([FromBody] IEnumerable<string> queries, [FromServices] QueryService service) =>
                await service.Query(queries))
            .WithName("Search by Query")
            .WithDescription("Retrieve files found by a tag query search")
            .WithOpenApi();

        app
            .MapPost("/files",
                async ([FromBody] ImportRequest request,
                    [FromServices] ImportRouter service,
                    CancellationToken token) => await service.ProcessImport(request, token))
            .WithName("Import")
            .WithDescription("Processes an import request")
            .WithOpenApi();

        app
            .MapPost("/files/deletion",
                async ([FromBody] DeleteRequest request, [FromServices] FileDeleter deleter) =>
                {
                    var results = await deleter.ProcessDeletion(request.Ids);

                    return new DeleteResponse(results);
                })
            .WithDescription("Delete one or more files and their associated data")
            .WithOpenApi();
    }

    private static void MapInfrastructureEndpoints(WebApplication app)
    {
        app
            .MapPost("/subscriptions",
                () => { throw new NotImplementedException("Subscription endpoint not yet implemented"); })
            .WithName("SubmitSubscription")
            .WithDescription("Submits a subscription request for automated queries")
            .WithOpenApi();

        app.MapPost("/clearAllData",
            async ([FromServices] ServerDbContext db) =>
            {
                db.Hashes.RemoveRange(db.Hashes);
                db.Mappings.RemoveRange(db.Mappings);
                db.Tags.RemoveRange(db.Tags);
                db.TagParents.RemoveRange(db.TagParents);
                db.TagSiblings.RemoveRange(db.TagSiblings);
                db.Namespaces.RemoveRange(db.Namespaces);
                db.Subtags.RemoveRange(db.Subtags);
                db.FileRecords.RemoveRange(db.FileRecords);

                await db.SaveChangesAsync();
            });

        app.MapHealthChecks("/health",
            new()
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync(report.Status.ToString());
                }
            });

        app
            .MapGet("/stats", async ([FromServices] StatsService statsService) => await statsService.GetHomeStats())
            .WithName("GetHomeStats")
            .WithDescription("Returns statistics for the homepage")
            .WithOpenApi();

        app
            .MapGet("/version",
                () => new
                {
                    Version = "1.0.0"
                })
            .WithName("GetVersion")
            .WithDescription("Returns the current API version")
            .WithOpenApi();

        app
            .MapGet("/config",
                ([FromServices] IConfiguration config) => new
                {
                    DatabaseRoot = config["DatabaseRoot"],
                    Environment = config["ASPNETCORE_ENVIRONMENT"],
                    LogLevel = config["Logging:LogLevel:Default"]
                })
            .WithName("GetConfig")
            .WithDescription("Returns non-sensitive configuration settings")
            .WithOpenApi();
    }
}