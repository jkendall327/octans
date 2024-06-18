using HydrusReplacement.Server.Models;
using HydrusReplacement.Server.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace HydrusReplacement.Server;

public static class Endpoints
{
    public static void AddEndpoints(this WebApplication app)
    {
        app.MapPost("/importFile", 
                async (ImportRequest request, FileService service) => await service.ImportFile(request))
            .WithName("ImportFile")
            .WithDescription("Import a single file from on-disk")
            .WithOpenApi();

        app.MapPost("/importFiles", (IEnumerable<Uri> filepath) => Results.Ok())
            .WithName("ImportFiles")
            .WithDescription("Import multiple files from on-disk")
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
        
        app.MapGet("/getFiles", (IEnumerable<int> id) => Results.Ok())
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
    }
}