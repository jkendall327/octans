using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Duplicates;

public class PerceptualHashProvider(ILogger<PerceptualHashProvider> logger) : IPerceptualHashProvider
{
    public Task<ulong> GetHash(Stream imageStream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["Service"] = nameof(PerceptualHashProvider) });
        logger.LogDebug("Calculating perceptual hash");
        var algorithm = new PerceptualHash();
        var hash = algorithm.Hash(imageStream);
        logger.LogDebug("Calculated perceptual hash {Hash}", hash);
        return Task.FromResult(hash);
    }
}
