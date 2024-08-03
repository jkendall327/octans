using MimeDetective.InMemory;

namespace HydrusReplacement.Core.Importing;

public interface IImportFilter
{
    Task<bool> PassesFilter(ImportRequest request, byte[] bytes, CancellationToken cancellationToken = default);
}

public class FiletypeFilter : IImportFilter
{
    public Task<bool> PassesFilter(ImportRequest request, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (request.AllowedFileTypes is null)
        {
            return Task.FromResult(true);
        }
        
        var type = bytes.DetectMimeType();

        var extension = type.Extension.ToLower();
        
        var valid = request.AllowedFileTypes
            .Select(filetype => filetype.ToLower())
            .Contains(extension);
        
        return Task.FromResult(valid);
    }
}

public class FilesizeFilter : IImportFilter
{
    public async Task<bool> PassesFilter(ImportRequest request, byte[] bytes, CancellationToken cancellationToken = default)
    {
        (var max, var min) = (request.MaxSize, request.MinSize);
        
        if (max is null && min is null)
        {
            return true;
        }

        if (max is not null && bytes.Length > max)
        {
            return false;
        }

        if (min is not null && bytes.Length < min)
        {
            return false;
        }

        return true;
    }
}