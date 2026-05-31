using System.Text.Json.Serialization;
using MudBlazor.Services;
using Octans.Client;
using Octans.Client.Components;
using Octans.Core.Http;
using Octans.Core.Http.Bandwidth;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("usersettings.json", optional: true, reloadOnChange: true);

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
builder.AddOctansObservability();
builder.Services.AddOctansClient();
builder.Services.AddInfrastructure();
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

builder.Services.AddDownloadManager(builder.Configuration.GetSection("Downloads"), options =>
{
    options.MaxConcurrentDownloads = 5;
    options.MaxConcurrentDownloadsPerDomain = 2;
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

namespace Octans.Client
{
    public partial class Program;
}
