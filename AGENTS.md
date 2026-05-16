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

## General notes

This is not a production app yet.
I have a dev database I don't want to explode, so use proper EF migrations.
But if I need to start up a new database for something really radical, it's not the end of the world.

## Verification

Use standard `dotnet build` and `dotnet test` to verify your work.

## Migrations

For EF migrations, use the data project as both the project and startup project so the design-time factory is used:
`DOTNET_ROOT=/home/jackkendall/.dotnet /home/jackkendall/.dotnet/tools/dotnet-ef migrations add <MigrationName> --project Octans.Data --startup-project Octans.Data`.

Don't get cute with trying to write your own migrations. Leverage the EF commands.
Unless you have good reason to, anyway.

## Code style
Use `var`.

Avoid excessive indentation; prefer early-returns and guard clauses.

Do not use reflection when writing tests. 

Instead of mocking out `ILogger<T>`, use `NullLogger<T>.Instance`.

Instead of mocking out `TimeProvider`, create `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

Instead of mocking out `IFilesystem`, create `MockFileSystem`.

Use collection expressions where appropriate. They are a new C# feature that looks like this:
```csharp
private readonly List<string> _foo = [];
private readonly List<string> _foo2 = ["test"];
var item1 = new HashItem { Hash = [1], PerceptualHash = 0 };
```