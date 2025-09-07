using System.Collections.Generic;
using System.Threading.Tasks;

namespace Octans.Core.Scripting;

public record CustomCommand(string Name, string Description, string Icon, Func<List<string>, Task> Execute);

public interface ICustomCommandProvider
{
    Task<List<CustomCommand>> GetCustomCommandsAsync();
}