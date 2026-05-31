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
    private readonly object _executionLock = new();
    private int _remainingHookCalls;
    private string _currentOperation = "Lua script";
    private CancellationToken _currentCancellationToken = CancellationToken.None;

    private DownloaderLuaContext(Lua lua)
    {
        _lua = lua;
        _instructionHook = (_, _) =>
        {
            if (_currentCancellationToken.IsCancellationRequested)
            {
                _lua.State.Error("Downloader {0} was canceled.", _currentOperation);
            }

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

    public object[] DoString(string script, string operation, CancellationToken cancellationToken = default) =>
        RunWithBudget(operation, () => _lua.DoString(script, operation), cancellationToken);

    public object[] Call(
        LuaFunction function,
        string operation,
        CancellationToken cancellationToken = default,
        params object[] args) =>
        RunWithBudget(operation, () => function.Call(args), cancellationToken);

    public void Dispose()
    {
        lock (_executionLock)
        {
            _lua.Dispose();
        }
    }

    private T RunWithBudget<T>(string operation, Func<T> action, CancellationToken cancellationToken)
    {
        // Downloader instances cache Lua contexts, so every call into an NLua state is serialized here.
        lock (_executionLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _currentOperation = operation;
            _currentCancellationToken = cancellationToken;
            _remainingHookCalls = Math.Max(1, MaxInstructionCount / InstructionHookInterval);
            _lua.State.SetHook(_instructionHook, LuaHookMask.Count, InstructionHookInterval);

            try
            {
                var result = action();
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception ex) when (ex is not DownloaderContractException and not OperationCanceledException)
            {
                throw new DownloaderContractException($"Downloader {operation} failed.", ex);
            }
            finally
            {
                _lua.State.SetHook(null, LuaHookMask.Disabled, 0);
                _currentOperation = "Lua script";
                _currentCancellationToken = CancellationToken.None;
            }
        }
    }
}
