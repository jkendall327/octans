using System.Text.Json.Serialization;
using MudBlazor.Services;
using Octans.Client;
using Octans.Client.Components;
using Octans.Core;
using Octans.Core.Models;
using Octans.Server;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using Octans.Core.Downloads;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});


// Setup keys
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "keys");

Directory.CreateDirectory(keysFolder);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new(keysFolder));

builder.Services.AddMudServices();
builder.Services.AddInfrastructure();
builder.Services.AddHttpClients();
builder.Services.AddOctansServices();
builder.Services.AddViewmodels();
builder.Services.AddBusinessServices();
builder.Services.AddChannels();
builder.Services.AddDatabase();

builder.Services.AddBandwidthLimiter(options =>
{
    // 1 MB/s
    options.DefaultBytesPerSecond = 1024 * 1024; 
});

builder.Services.AddDownloadManager(options =>
{
    options.MaxConcurrentDownloads = 5;
});

builder.SetupConfiguration();

builder.Services.Configure<ServiceProviderOptions>(sp =>
{
    sp.ValidateScopes = true;
    sp.ValidateOnBuild = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();

var supportedCultures = new[] { "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.AddEndpoints();
app.MapImageEndpoints();

// Ensure subfolders are initialised.
var manager = app.Services.GetRequiredService<SubfolderManager>();
manager.MakeSubfolders();

// Ensure database is initialised.
var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
await db.Database.EnsureCreatedAsync();
scope.Dispose();

await app.RunAsync();

public partial class Program;
