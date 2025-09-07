using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Progress;

namespace Octans.Core.Importing;

public sealed class ImportFolderBackgroundService(
    IOptions<ImportFolderOptions> options,
    IServiceScopeFactory scopeFactory,
    IFileSystem fileSystem,
    IBackgroundProgressReporter progressReporter,
    ILogger<ImportFolderBackgroundService> logger) : BackgroundService
{
    private readonly string[] _importFolders = options.Value.Directories.ToArray();
    private static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".gif"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Period);

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
            DeleteAfterImport = options.Value.DeleteAfterImport,
            FilterData = new()
            {
                AllowedFileTypes = ImageExtensions
            }
        };

        var handle = await progressReporter.Start("Import folder scan", _importFolders.Length);
        var processed = 0;

        foreach (var folder in _importFolders)
        {
            if (!fileSystem.Directory.Exists(folder))
            {
                logger.LogWarning("Import folder does not exist: {Folder}", folder);
                processed++;
                await progressReporter.Report(handle.Id, processed);

                continue;
            }

            var imports = fileSystem
                .Directory
                .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(IsImageFile)
                .Select(file => new ImportItem
                {
                    Url = new(file)
                })
                .ToList();

            request.Items.AddRange(imports);
            processed++;
            await progressReporter.Report(handle.Id, processed);
        }

        await progressReporter.Complete(handle.Id);
        await SendImportRequest(request, stoppingToken);
    }

    private async Task SendImportRequest(ImportRequest importRequest, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var router = scope.ServiceProvider.GetRequiredService<Importer>();

            var response = await router.ProcessImport(importRequest, stoppingToken);

            logger.LogInformation("Sent import request for {ImportCount} items", importRequest.Items.Count);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Error sending import request to API");
        }
    }

    private bool IsImageFile(string filePath)
    {
        var extension = fileSystem
            .Path
            .GetExtension(filePath)
            .ToUpperInvariant();

        return ImageExtensions.Contains(extension);
    }
}