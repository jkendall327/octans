using System.IO.Abstractions;
using Octans.Client;
using Octans.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();

app.Run();