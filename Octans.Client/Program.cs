using System.Text.Json.Serialization;
using Octans.Client;
using Octans.Client.Components;
using Octans.Core;
using Octans.Core.Models;
using Octans.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddInfrastructure();
builder.Services.AddHttpClients();
builder.Services.AddOctansServices();
builder.Services.AddViewmodels();
builder.AddBusinessServices();
builder.AddOptions();
builder.AddChannels();
builder.AddDatabase();

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
