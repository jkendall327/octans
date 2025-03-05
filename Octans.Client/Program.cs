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
app.MapStaticAssets();
app.MapHealthChecks("/health", new()
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(report.Status.ToString());
    }
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

namespace Octans.Client
{
    public partial class Program
    {
    }
}

