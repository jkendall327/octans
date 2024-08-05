using HydrusReplacement.Core;
using HydrusReplacement.Core.Importing;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Core.Models.Tagging;
using HydrusReplacement.Server.Services;

namespace HydrusReplacement.Server;

public static class Endpoints
{
    public static void AddEndpoints(this WebApplication app)
    {
        app.MapPost("/import", async (ImportRequest request, Importer service) => await service.ProcessImport(request))
            .WithName("Import")
            .WithDescription("Processes an import request")
            .WithOpenApi();

        app.MapGet("/getFile", async (int id, FileFinder service) =>
            {
                var file = await service.GetFile(id);

                return file is null ? Results.NotFound() : Results.Ok(file);
            })
            .WithName("GetFile")
            .WithDescription("Get a single file by its ID")
            .WithOpenApi();

        app.MapGet("/query", async (IEnumerable<Tag> tags, FileFinder service) =>
            {
                var files = await service.GetFilesByTagQuery(tags);

                if (files is null || !files.Any())
                {
                    return Results.NotFound();
                }

                return Results.Ok(files);
            })
            .WithName("Search by Query")
            .WithDescription("Retrieve files found by a tag query search")
            .WithOpenApi();
        
        app.MapPut("/updateTags", async (UpdateTagsRequest request, TagUpdater updater) =>
            {
                var success = await updater.UpdateTags(request);
                return success ? Results.Ok() : Results.NotFound();
            })
            .WithName("UpdateTags")
            .WithDescription("Add and remove tags for a specific image")
            .WithOpenApi();
        
        app.MapPost("/delete", async (DeleteRequest request, ItemDeleter deleter) =>
            {
                var results = await deleter.ProcessDeletion(request);

                var response = new DeleteResponse(request.DeleteId, results);
                
                return Results.Ok(response);
            })
            .WithName("DeleteFiles")
            .WithDescription("Delete one or more files and their associated data")
            .WithOpenApi();

        app.MapPost("/clearAllData",
            async (ServerDbContext db) =>
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
        
        app.MapHealthChecks("/health", new()
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(report.Status.ToString());
            }
        });
        
        app.MapGet("/version", () => new { Version = "1.0.0" })
            .WithName("GetVersion")
            .WithDescription("Returns the current API version")
            .WithOpenApi();
        
        app.MapGet("/config", (IConfiguration config) => 
                new
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