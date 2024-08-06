using Octans.Client;
using Octans.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddScoped<SubfolderManager>();

builder.Services.AddHttpClient("ServerApi", (client) =>
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