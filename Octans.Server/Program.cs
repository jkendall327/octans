using System.Text.Json.Serialization;
using Octans.Core;
using Octans.Core.Communication;
using Octans.Core.Models;
using Octans.Server;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(s
    => s.ListenLocalhost(CommunicationConstants.OCTANS_SERVER_PORT));

builder.Services
    .AddOptions<ServiceProviderOptions>()
    .Configure(options =>
    {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    });

builder.Services.ConfigureHttpJsonOptions(j =>
{
    j.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

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

await app.RunAsync();

public partial class Program { }