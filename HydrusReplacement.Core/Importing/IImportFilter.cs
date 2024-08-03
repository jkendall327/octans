using MimeDetective.InMemory;
using SixLabors.ImageSharp;

namespace HydrusReplacement.Core.Importing;

public interface IImportFilter
{
    Task<bool> PassesFilter(ImportRequest request, byte[] bytes, CancellationToken cancellationToken = default);
}

public class ResolutionFilter : IImportFilter
{
    public async Task<bool> PassesFilter(ImportRequest request, byte[] bytes, CancellationToken cancellationToken = default)
    {
        var image = Image.Load(bytes);

        (var height, var width) = (image.Height, image.Width);

        if (request.MinHeight is not null && request.MinHeight > height)
        {
            return false;
        }
        
        if (request.MinWidth is not null && request.MinWidth > width)
        {
            return false;
        }

        if (request.MaxHeight is not null && request.MaxHeight < height)
        {
            return false;
        }

        if (request.MaxWidth is not null && request.MaxWidth < height)
        {
            return false;
        }
        
        return true;
    }
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
        (var max, var min) = (request.MaxFileSize, request.MinFileSize);
        
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