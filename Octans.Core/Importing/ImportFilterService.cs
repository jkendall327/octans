using Microsoft.Extensions.Logging;
using Octans.Core.Importing;

namespace Octans.Server;

public sealed class ImportFilterService
{
    private readonly ILogger<ImportFilterService> _logger;

    public ImportFilterService(ILogger<ImportFilterService> logger)
    {
        _logger = logger;
    }

    public async Task<ImportItemResult?> ApplyFilters(ImportRequest request, byte[] bytes)
    {
        if (request.FilterData is null)
        {
            return null;
        }

        var filters = new List<IImportFilter>
        {
            new FilesizeFilter(),
            new FiletypeFilter(),
            new ResolutionFilter()
        };

        foreach (var filter in filters)
        {
            var result = await filter.PassesFilter(request.FilterData, bytes);

            _logger.LogDebug("{FilterName} result: {FilterResult}", filter.GetType().Name, result);

            if (result) continue;
            
            _logger.LogInformation("File rejected by import filters");

            return new()
            {
                Ok = false,
                Message = $"Failed {filter.GetType().Name} filter"
            };
        }

        return null;
    }
}