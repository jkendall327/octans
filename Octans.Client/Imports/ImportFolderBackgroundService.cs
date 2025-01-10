using System.IO.Abstractions;
using Octans.Core.Importing;

namespace Octans.Client;

internal sealed class ImportFolderBackgroundService(
    IConfiguration configuration,
    ServerClient client,
    IFileSystem fileSystem,
    ILogger<ImportFolderBackgroundService> logger) : BackgroundService
{
    private readonly string[] _importFolders = configuration.GetValue<string[]>("importFolders") ?? [];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogInformation("Checking for files in import folders...");

            await ScanAndImportFolders(stoppingToken);
        }
    }

    private async Task ScanAndImportFolders(CancellationToken stoppingToken)
    {
        var request = new ImportRequest
        {
            Items = new(),
            ImportType = ImportType.File,
            DeleteAfterImport = true,
            FilterData = new()
            {
                AllowedFileTypes = [".jpg", ".jpeg", ".png", ".gif"]
            }
        };

        foreach (var folder in _importFolders)
        {
            if (!fileSystem.Directory.Exists(folder))
            {
                logger.LogWarning("Import folder does not exist: {Folder}", folder);
                continue;
            }

            var imports = fileSystem.Directory
                .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(IsImageFile)
                .Select(file => new ImportItem
                {
                    Source = new(file)
                }).ToList();

            request.Items.AddRange(imports);
        }

        await SendImportRequest(request, stoppingToken);
    }

    private async Task SendImportRequest(ImportRequest importRequest, CancellationToken stoppingToken)
    {
        try
        {
            var response = await client.Import(importRequest, stoppingToken);

            logger.LogInformation("Sent import request for {ImportCount} items", importRequest.Items.Count);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Error sending import request to API");
        }
    }

    private bool IsImageFile(string filePath)
    {
        var extension = fileSystem.Path.GetExtension(filePath).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }
}