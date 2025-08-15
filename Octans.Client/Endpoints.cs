using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
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

                    return success is TagUpdateResult.TagsUpdated ? Results.Ok() : Results.BadRequest();
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
                ([FromBody] IEnumerable<string> queries, [FromServices] QueryService service) => service.Query(queries))
            .WithName("Search by Query")
            .WithDescription("Retrieve files found by a tag query search")
            .WithOpenApi();

        app
            .MapPost("/files",
                async ([FromBody] ImportRequest request,
                    [FromServices] IImporter service,
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
    }

    public static void MapImageEndpoints(this WebApplication app)
    {
        app.MapGet("/media/{hash}",
            (HttpContext http, string hash, [FromServices] SubfolderManager manager) =>
            {
                if (string.IsNullOrWhiteSpace(hash))
                {
                    return Results.BadRequest("Invalid hash.");
                }

                byte[] unhashedBytes;

                try
                {
                    unhashedBytes = Convert.FromHexString(hash);
                }
                catch
                {
                    return Results.BadRequest("Hash must be hex.");
                }

                var info = manager.GetFilepath(HashedBytes.FromHashed(unhashedBytes));

                if (info is null || !info.Exists)
                {
                    return Results.NotFound();
                }

                var provider = new FileExtensionContentTypeProvider();

                if (!provider.TryGetContentType(info.FullName, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                // ETag derived from the content hash you're already using.
                // (Quotes are required around the tag string.)
                var etag = new EntityTagHeaderValue($"\"{hash.ToLowerInvariant()}\"");

                http.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";

                return Results.File(path: info.FullName,
                    contentType: contentType,
                    lastModified: info.LastWriteTimeUtc,
                    entityTag: etag,
                    enableRangeProcessing: true);
            });
    }
}