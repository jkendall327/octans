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

        app.MapGet("/getFile", async (int id, ServerDbContext context) =>
            {
                var file = await context.FindAsync<FileRecord>(id);
                return Results.Ok(file);
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

    private static async Task<IResult> ImportFile(Uri filepath, ServerDbContext context)
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var files = Directory.CreateDirectory(Path.Join(baseDirectory, "db", "files"));

        var fileName = Path.GetFileName(filepath.AbsolutePath);
        var destination = Path.Join(files.FullName, fileName);
        File.Copy(filepath.AbsolutePath, destination);

        var record = new FileRecord { Filepath = destination };

        context.FileRecords.Add(record);
        await context.SaveChangesAsync();

        return Results.Ok(record);
    }
}