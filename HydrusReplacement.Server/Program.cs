using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Server;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<ServerDbContext>(s =>
{
    if (builder.Environment.IsDevelopment())
    {
        s.UseInMemoryDatabase("db");
    }
    else
    {
        var dbFolder = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "db");

        var db = Path.Join(dbFolder, "server.db");
        
        s.UseSqlite($"Data Source={db};");
    }
});

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

app.Run();