using HydrusReplacement.Server.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ServerDbContext>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var files = Directory.CreateDirectory(Path.Join(AppDomain.CurrentDomain.BaseDirectory, "db", "files"));

app.MapPost("/importFile", async (Uri filepath, ServerDbContext context) =>
    {
        var destination = Path.Join(files.FullName, filepath.ToString());
        File.Copy(filepath.ToString(), destination);
        var id = Random.Shared.Next();

        var record = new FileRecord
        {
            Id = id,
            Filepath = destination
        };

        context.FileRecords.Add(record);
        await context.SaveChangesAsync();
        
        return Results.Ok();
    })
    .WithName("ImportFile")
    .WithDescription("Import a single file from on-disk")
    .WithOpenApi();

app.MapPost("/importFiles", (IEnumerable<Uri> filepath) => Results.Ok())
    .WithName("ImportFiles")
    .WithDescription("Import multiple files from on-disk")
    .WithOpenApi();


app.MapGet("/getFile", (int id) => Results.Ok())
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

app.Run();