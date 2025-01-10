using System.IO.Abstractions;
using Octans.Client;
using Octans.Client.Components;
using Octans.Client.Components.Pages;
using Octans.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddScoped<SubfolderManager>();
builder.Services.AddSingleton<IFileSystem>(new FileSystem());
builder.Services.AddSingleton<ImportRequestSender>();

builder.Services.Configure<GlobalSettings>(builder.Configuration.GetSection(nameof(GlobalSettings)));

builder.Services.AddHttpClient<ServerClient>(client =>
{
    var port = CommunicationConstants.OCTANS_SERVER_PORT;
    client.BaseAddress = new($"http://localhost:{port}/");
});

builder.Services.AddScoped<GalleryViewmodel>();

builder.Services.AddHostedService<ImportFolderBackgroundService>();

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