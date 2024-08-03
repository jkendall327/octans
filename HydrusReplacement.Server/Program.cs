using HydrusReplacement.Core;
using HydrusReplacement.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

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

app.Run();