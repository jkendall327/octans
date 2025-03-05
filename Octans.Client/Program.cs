using Octans.Client;
using Octans.Client.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure();
builder.Services.AddHttpClients();
builder.Services.AddOctansServices();
builder.Services.AddViewmodels();

builder.SetupConfiguration();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();