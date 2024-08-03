using HydrusReplacement.Server;
using HydrusReplacement.Server.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<ServerDbContext>();

builder.Services.AddScoped<SubfolderManager>();
builder.Services.AddScoped<FileService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.AddEndpoints();

// Ensure subfolders are initialised
using (var scope = app.Services.CreateScope())
{
    var manager = scope.ServiceProvider.GetRequiredService<SubfolderManager>();
    manager.MakeSubfolders();
}

// await ClearDatabase();

app.Run();

return;

// Testing convenience.
async Task ClearDatabase()
{
    await using var db = new ServerDbContext();

    db.Hashes.RemoveRange(db.Hashes);
    db.Mappings.RemoveRange(db.Mappings);
    db.Tags.RemoveRange(db.Tags);
    db.TagParents.RemoveRange(db.TagParents);
    db.TagSiblings.RemoveRange(db.TagSiblings);
    db.Namespaces.RemoveRange(db.Namespaces);
    db.Subtags.RemoveRange(db.Subtags);
    db.FileRecords.RemoveRange(db.FileRecords);

    await db.SaveChangesAsync();
}