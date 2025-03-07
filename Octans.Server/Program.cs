using System.Text.Json.Serialization;
using Octans.Core;
using Octans.Core.Communication;
using Octans.Core.Downloads;
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

builder.Services.AddBandwidthLimiter(options => {
    options.DefaultBytesPerSecond = 1024 * 1024; // 1 MB/s
});

builder.Services.AddDownloadManager(options => {
    options.MaxConcurrentDownloads = 5;
});

builder.Services.AddHostedService<DownloadManager>();

builder.Services.AddHostedService<ThumbnailCreationBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.AddEndpoints();

var s = app.Services.GetRequiredService<DownloadService>();

await s.QueueDownloadAsync(new() {
    Url = "https://example.com/file.zip",
    DestinationPath = "C:/Downloads/file.zip"
});

return;

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