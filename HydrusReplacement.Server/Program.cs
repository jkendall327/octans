using System.IO.Abstractions;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Server;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

var filesystem = new FileSystem();

builder.Services.AddSingleton(filesystem.Path);
builder.Services.AddSingleton(filesystem.DirectoryInfo);

builder.Services.AddDbContext<ServerDbContext>((s, opt) =>
{
    var path = s.GetRequiredService<IPath>();
    
    if (builder.Environment.IsDevelopment())
    {
        opt.UseInMemoryDatabase("db");
    }
    else
    {
        var dbFolder = path.Join(AppDomain.CurrentDomain.BaseDirectory, "db");

        var db = path.Join(dbFolder, "server.db");
        
        opt.UseSqlite($"Data Source={db};");
    }
});

builder.Services.AddScoped<SubfolderManager>();
builder.Services.AddScoped<FileFinder>();
builder.Services.AddScoped<Importer>();

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