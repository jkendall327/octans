using MimeDetective.InMemory;
using SixLabors.ImageSharp;

namespace Octans.Core.Importing;

public interface IImportFilter
{
    Task<bool> PassesFilter(ImportFilterData request, byte[] bytes, CancellationToken cancellationToken = default);
}

public class ResolutionFilter : IImportFilter
{
    public Task<bool> PassesFilter(ImportFilterData request, byte[] bytes, CancellationToken cancellationToken = default)
    {
        var image = Image.Load(bytes);

        (var height, var width) = (image.Height, image.Width);

        if (request.MinHeight is not null && request.MinHeight > height)
        {
            return Task.FromResult(false);
        }

        if (request.MinWidth is not null && request.MinWidth > width)
        {
            return Task.FromResult(false);
        }

        if (request.MaxHeight is not null && request.MaxHeight < height)
        {
            return Task.FromResult(false);
        }

        if (request.MaxWidth is not null && request.MaxWidth < height)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}

public class FiletypeFilter : IImportFilter
{
    public Task<bool> PassesFilter(ImportFilterData request, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (request.AllowedFileTypes is null)
        {
            return Task.FromResult(true);
        }

        var type = bytes.DetectMimeType();

        var extension = type.Extension.ToLowerInvariant();

        var valid = request.AllowedFileTypes
            .Select(filetype => filetype.ToLowerInvariant())
            .Contains(extension);

        return Task.FromResult(valid);
    }
}

public class FilesizeFilter : IImportFilter
{
    public Task<bool> PassesFilter(ImportFilterData request, byte[] bytes, CancellationToken cancellationToken = default)
    {
        (var max, var min) = (request.MaxFileSize, request.MinFileSize);

        if (max is null && min is null)
        {
            return Task.FromResult(true);
        }

        if (max is not null && bytes.Length > max)
        {
            return Task.FromResult(false);
        }

        if (min is not null && bytes.Length < min)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}