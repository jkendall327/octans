namespace HydrusReplacement.Core.Importing;

public interface IImportFilter
{
    public string Id { get; }
    Task<bool> PassesFilter(ImportRequest request, byte[] bytes, CancellationToken cancellationToken = default);
}

public class FilesizeFilter : IImportFilter
{
    public string Id => nameof(FilesizeFilter);
    
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