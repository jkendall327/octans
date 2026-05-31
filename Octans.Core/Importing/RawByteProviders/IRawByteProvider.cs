namespace Octans.Core.Importing.RawByteProviders;

internal interface IRawByteProvider
{
    Task<byte[]> GetRawBytes(ImportItem item);
}
