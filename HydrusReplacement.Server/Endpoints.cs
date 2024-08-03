using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace HydrusReplacement.Server;

public static class Endpoints
{
    public static void AddEndpoints(this WebApplication app)
    {
        app.MapPost("/import", async (ImportRequest request, FileService service) => await service.ProcessImport(request))
            .WithName("Import")
            .WithDescription("Processes an import request")
            .WithOpenApi();

        app.MapGet("/getFile", async (int id, FileService service) =>
            {
                var file = await service.GetFile(id);

                return file is null ? Results.NotFound() : Results.Ok(file);
            })
            .WithName("GetFile")
            .WithDescription("Get a single file by its ID")
            .WithOpenApi();

        app.MapGet("/getAll", async (int? limit, ServerDbContext context) =>
            {
                var hashes = context.Hashes.AsQueryable();

                if (limit is not null)
                {
                    hashes = hashes.Take(limit.Value);
                }
                
                var results = await hashes.ToListAsync();

                return results.Any() ? Results.Ok(results) : Results.NotFound();
            })
            .WithName("GetAllFiles")
            .WithDescription("Get all files (limit optional)")
            .WithOpenApi();
        
        app.MapGet("/getFiles", (IEnumerable<int> ids) => Results.Ok())
            .WithName("GetFiles")
            .WithDescription("Get multiple files by their IDs")
            .WithOpenApi();

        app.MapGet("/query", async (IEnumerable<Tag> tags, FileService service) =>
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
    }
}