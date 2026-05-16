## Repository Map

- `Octans.Core/Communication` – API client interfaces and state change notifications.
- `Octans.Core/Downloads` – download queue, bandwidth limiting, and individual downloaders in `Downloads/Downloaders`.
- `Octans.Core/Duplicates` – perceptual hashing to detect duplicate files.
- `Octans.Core/Extensions` – project-wide extension methods.
- `Octans.Core/Filesystem` – helpers for managing storage folders, deleting stuff etc.
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

## What is this app?

My reimplementation of the Hydrus Network, a tag-based image archival/storage/viewer.

Features of note:
- Content importing. Content can come from the local filesystem or downloaded from the web.
- Subscriptions. Periodic web scans of sources for content.
- Tags/querying. See `docs/Querying.md` if curious.
- Duplicates. Scanning for near-duplicate images based on perceptual hashes.
- Downloaders. User-created scripts for interfacing with arbitrary websites via Lua.
- HTTP downloading. Distinct from the above. Feature-agnostic subsystem for managing HTTP requests, respecting site bandwidth limits, etc.

See `docs/Architectural decisions.md` for explanations of why certain tech or design patterns were used.
See `docs/Terminology.md` for clarifications of what certain words mean in this project.

## What changes are appropriate?

This is not a production app yet.
I have a dev database I don't want to explode, so use proper EF migrations.
But if I need to start up a new database for something really radical, it's not the end of the world.

I'm open to changing the design of things in small and big ways.

I'm specifically not wedded to using Blazor for the frontend.
I'm toying with a React frontend instead for simplicity, hence the currently not-really-used API endpoints.

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