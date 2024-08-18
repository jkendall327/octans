using Octans.Core;
using Octans.Core.Models;
using Octans.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.AddOptions();
builder.AddFilesystem();
builder.AddChannels();
builder.AddDatabase();
builder.AddBusinessServices();

builder.Services.AddHostedService<ThumbnailCreationBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.AddEndpoints();

// Ensure subfolders are initialised.
var manager = app.Services.GetRequiredService<SubfolderManager>();
manager.MakeSubfolders();

// Ensure database is initialised.
var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
await db.Database.EnsureCreatedAsync();
scope.Dispose();

app.Run();

public partial class Program {}