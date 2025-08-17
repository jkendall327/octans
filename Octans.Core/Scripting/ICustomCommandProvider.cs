namespace Octans.Core.Scripting;

public record CustomCommand(string Name, string Description, string Icon, Func<string, Task> Execute);

public interface ICustomCommandProvider
{
    Task<List<CustomCommand>> GetCustomCommandsAsync();
}