using System.IO.Abstractions;
using Octans.Client;
using Octans.Client.Components;
using Octans.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddHttpClient();

builder.Services.AddScoped<SubfolderManager>();
builder.Services.AddSingleton<IFileSystem>(new FileSystem());
builder.Services.AddSingleton<ImportRequestSender>();

builder.Services.Configure<GlobalSettings>(builder.Configuration.GetSection("GlobalSettings"));

builder.Services.AddHttpClient<ServerClient>(client =>
{
    var port = CommunicationConstants.OCTANS_SERVER_PORT;
    client.BaseAddress = new($"http://localhost:{port}/");
});

builder.Services.AddHostedService<ImportFolderBackgroundService>();

builder.Services.Configure<ServiceProviderOptions>(s =>
{
    s.ValidateOnBuild = true;
    s.ValidateScopes = true;
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
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();