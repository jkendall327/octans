using System.IO.Abstractions;
using Octans.Client;
using Octans.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddScoped<SubfolderManager>();

var filesystem = new FileSystem();

builder.Services.AddSingleton(filesystem.DirectoryInfo);
builder.Services.AddSingleton(filesystem.Path);
builder.Services.AddSingleton(filesystem.Directory);

builder.Services.Configure<GlobalSettings>(builder.Configuration.GetSection("GlobalSettings"));

builder.Services.AddHttpClient<ServerClient>(client =>
{
    client.BaseAddress = new("http://localhost:5185");
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