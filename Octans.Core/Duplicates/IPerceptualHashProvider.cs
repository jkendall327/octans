using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Octans.Core.Duplicates;

public interface IPerceptualHashProvider
{
    Task<ulong> GetHash(Stream imageStream, CancellationToken cancellationToken = default);
}
