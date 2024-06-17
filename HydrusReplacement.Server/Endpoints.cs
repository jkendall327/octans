using System.Security.Cryptography;
using HydrusReplacement.Server.Models;

namespace HydrusReplacement.Server;

public static class Endpoints
{
    public static void AddEndpoints(this WebApplication app)
    {

        app.MapPost("/importFile", ImportFile)
            .WithName("ImportFile")
            .WithDescription("Import a single file from on-disk")
            .WithOpenApi();

        app.MapPost("/importFiles", (IEnumerable<Uri> filepath) => Results.Ok())
            .WithName("ImportFiles")
            .WithDescription("Import multiple files from on-disk")
            .WithOpenApi();

        app.MapGet("/getFile", async (int id, SubfolderManager manager, ServerDbContext context) =>
            {
                var hash = await context.FindAsync<HashItem>(id);

                var subfolder = manager.GetSubfolder(hash.Hash);
                
                return Results.Ok(hash);
            })
            .WithName("GetFile")
            .WithDescription("Get a single file by its ID")
            .WithOpenApi();

        app.MapGet("/getFiles", (IEnumerable<int> id) => Results.Ok())
            .WithName("GetFiles")
            .WithDescription("Get multiple files by their IDs")
            .WithOpenApi();

        app.MapGet("/query", (IEnumerable<string> tags) => Results.Ok())
            .WithName("Search by Query")
            .WithDescription("Retrieve files found by a tag query search")
            .WithOpenApi();
    }

    private static async Task<IResult> ImportFile(Uri filepath, SubfolderManager manager, ServerDbContext context)
    {
        var bytes = await File.ReadAllBytesAsync(filepath.AbsolutePath);
        var hashed = SHA256.HashData(bytes);

        var subfolder = manager.GetSubfolder(hashed);
        
        Directory.CreateDirectory(SubfolderManager.HashFolderPath);

        var fileName = Path.GetFileName(filepath.AbsolutePath);
        var destination = Path.Join(subfolder.AbsolutePath, fileName);
        File.Copy(filepath.AbsolutePath, destination);
        
        var record = new HashItem { Hash = hashed };

        context.Hashes.Add(record);
        await context.SaveChangesAsync();

        return Results.Ok(record);
    }
}