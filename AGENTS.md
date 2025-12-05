## Repository Map

- `Octans.Core/Communication` – API client interfaces and state change notifications.
- `Octans.Core/Deletion` – utilities for removing files from storage.
- `Octans.Core/Downloads` – download queue, bandwidth limiting, and individual downloaders in `Downloads/Downloaders`.
- `Octans.Core/Duplicates` – perceptual hashing to detect duplicate files.
- `Octans.Core/Extensions` – project-wide extension methods.
- `Octans.Core/Filesystem` – helpers for managing storage folders.
- `Octans.Core/Importing` – pipeline for importing files into the repository.
- `Octans.Core/Progress` – background progress tracking utilities.
- `Octans.Core/Querying` – parsing and executing search queries.
- `Octans.Core/Repositories` – services for tracking repository changes.
- `Octans.Core/Scripting` – support for custom command execution.
- `Octans.Core/Stats` – compute application statistics and storage usage.
- `Octans.Core/Subscriptions` – background processing for subscriptions.
- `Octans.Core/Tags` – tag models and manipulation utilities.
- `Octans.Core/Thumbnails` – generating and managing thumbnails.
- `Octans.Client/Components` – Blazor UI components for downloads, gallery, and settings.
- `Octans.Data` – Entity Framework Core models and migrations.
- `Octans.Tests` – unit tests covering core services and client view models.

## Code style
Prefer 'var' over specifying the type explicitly.

Avoid excessive indentation; prefer early-returns and guard clauses.

Use System.Thread.Lock for creating locks: this is a new .NET feature. They look like this:

```csharp
public class Foo(ILogger<Foo> logger)
{
    private readonly Lock _lock = new();

    public void Example()
    {
        lock (_lock)
        {
            // ...
        }
    }
}

```

Do NOT convert uses of System.Thread.Lock back to locking on a standard `object`.

Use primary constructors. Primary constructors are a new feature in .NET. They look like this:

```csharp
public class Foo(ILogger<Foo> logger)
{
    public void Example() => logger.LogInformation("Test");
}
```

Do NOT convert primary constructors back to traditional constructors.

## Testable code
When working with dates or times, inject `System.TimeProvider` as a clock abstraction.

```csharp
public class Foo(TimeProvider clock)
{
    public void Example()
    {
        var now = _timeProvider.GetUtcNow();
        Console.WriteLine($"Current time: {now}");
    }
}
```

Do NOT use DateTime.Now, DateTime.UtcNow etc.

When working with the filesystem, inject an `IFileSystem` from `System.IO.Abstractions` or a more specific type from that package.

```csharp
public class Foo(IFileSystem filesystem)
{
    public async Task Example()
    {
        await filesystem.File.WriteAllTextAsync("foo.txt", "Hello world");
    }
}
```

## Tests
Do NOT use 'Arrange - Act - Assert' comments in tests.

Use NSubstitute, not Moq.

Do not use reflection when writing tests. 
If a test would require reflection, either don't implement it or change the SUT so reflection is not required. 

Instead of mocking out `ILogger<T>`, use `NullLogger<T>.Instance` instead.
Instead of mocking out `TimeProvider`, create a new `Microsoft.Extensions.Time.Testing.FakeTimeProvider` instead.
Instead of mocking out `IFilesystem`, create a new `MockFileSystem` instead.

Name the variable for the system under test as `_sut` (if a field) or `sut` (a local variable).

Do not make assertions based on log message output.

## Comments
Avoid pointless comments that simply explain what the code is doing.

Place comments on the line above what they describe, not in-line.

## Logging

Use structured logging, i.e. `logger.LogInformation("Foo: {Value}", value);`

Avoid pointless logs - only add what's meaningful.

Use `.BeginScope()` with a `Dictionary<string, object?>` when logging if it cuts down on repetition.