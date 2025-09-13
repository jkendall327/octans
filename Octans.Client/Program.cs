using System.Text.Json.Serialization;
using MudBlazor.Services;
using Octans.Client;
using Octans.Client.Components;
using Octans.Core;
using Octans.Core.Models;
using Octans.Server;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using Microsoft.EntityFrameworkCore;
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

builder.AddKeyProtection();

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

app.SetupLocalisation();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.AddEndpoints();
app.MapImageEndpoints();

await app.PerformAppInitialisation();

await app.RunAsync();

public partial class Program;
