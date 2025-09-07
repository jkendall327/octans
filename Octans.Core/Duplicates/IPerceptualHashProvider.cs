namespace Octans.Core.Duplicates;

public interface IPerceptualHashProvider
{
    Task<ulong> GetHash(Stream imageStream, CancellationToken cancellationToken = default);
}
