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
Use `var`.

Avoid excessive indentation; prefer early-returns and guard clauses.

Do not use reflection when writing tests. 

Instead of mocking out `ILogger<T>`, use `NullLogger<T>.Instance`.

Instead of mocking out `TimeProvider`, create `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

Instead of mocking out `IFilesystem`, create `MockFileSystem`.