using HydrusReplacement.Server;
using HydrusReplacement.Server.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<ServerDbContext>();
builder.Services.AddScoped<SubfolderManager>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.AddEndpoints();

new SubfolderManager().MakeSubfolders();

app.Run();