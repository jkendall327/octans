var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/importFile", (Uri filepath) => Results.Ok())
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