using KeraLua;
using Lua = NLua.Lua;
using LuaFunction = NLua.LuaFunction;
using LuaTable = NLua.LuaTable;

namespace Octans.Core.Downloaders;

internal sealed class DownloaderLuaContext : IDisposable
{
    private const int InstructionHookInterval = 10_000;
    private const int MaxInstructionCount = 1_000_000;

    private const string SandboxScript = """
                                         luanet = nil
                                         import = nil
                                         CLRPackage = nil
                                         io = nil
                                         os = nil
                                         package = nil
                                         require = nil
                                         dofile = nil
                                         loadfile = nil
                                         load = nil
                                         debug = nil
                                         collectgarbage = nil
                                         """;

    private readonly Lua _lua;
    private readonly LuaHookFunction _instructionHook;
    private int _remainingHookCalls;
    private string _currentOperation = "Lua script";

    private DownloaderLuaContext(Lua lua)
    {
        _lua = lua;
        _instructionHook = (_, _) =>
        {
            _remainingHookCalls--;
            if (_remainingHookCalls > 0)
            {
                return;
            }

            _lua.State.Error("Downloader {0} exceeded the Lua instruction budget.", _currentOperation);
        };
    }

    public static DownloaderLuaContext Create()
    {
        var lua = new Lua();
        lua.DoString(SandboxScript, "octans_downloader_sandbox");
        return new(lua);
    }

    public LuaFunction GetFunction(string functionName) =>
        _lua[functionName] as LuaFunction ??
        throw new DownloaderContractException($"{functionName} not found in Lua downloader script.");

    public LuaTable? GetTable(string tableName) => _lua.GetTable(tableName);

    public object[] DoString(string script, string operation) =>
        RunWithBudget(operation, () => _lua.DoString(script, operation));

    public object[] Call(LuaFunction function, string operation, params object[] args) =>
        RunWithBudget(operation, () => function.Call(args));

    public void Dispose()
    {
        _lua.Dispose();
    }

    private T RunWithBudget<T>(string operation, Func<T> action)
    {
        _currentOperation = operation;
        _remainingHookCalls = Math.Max(1, MaxInstructionCount / InstructionHookInterval);
        _lua.State.SetHook(_instructionHook, LuaHookMask.Count, InstructionHookInterval);

        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not DownloaderContractException)
        {
            throw new DownloaderContractException($"Downloader {operation} failed.", ex);
        }
        finally
        {
            _lua.State.SetHook(null, LuaHookMask.Disabled, 0);
            _currentOperation = "Lua script";
        }
    }
}
