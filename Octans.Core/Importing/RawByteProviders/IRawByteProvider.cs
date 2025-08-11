namespace Octans.Core.Importing.RawByteProviders;

public interface IRawByteProvider
{
    Task<byte[]> GetRawBytes(ImportItem item);
}