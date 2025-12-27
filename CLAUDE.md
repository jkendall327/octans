# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Octans Development Guide

## Commands

### Validation (run after any change)
- **Full validation**: `./validate.sh` (Linux/Mac) or `./validate.ps1` (Windows)
- This runs build + tests and confirms all checks pass with exit code 0

### Individual Commands
- Build: `dotnet build`
- Run tests: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=Octans.Tests.YourTestNamespace.YourTestClass.YourTestMethod"`
- Run tests for a class: `dotnet test --filter "FullyQualifiedName~YourTestClass"`
- Run client: `dotnet run --project Octans.Client`

### Validation Expectations
- Build must complete with zero warnings (warnings are errors)
- All tests must pass
- No nullable reference warnings (CS8600-CS8625 are errors)

## Project Architecture

### Core Projects
- **Octans.Core**: Business logic, downloading, importing, querying, thumbnails, and tag management
- **Octans.Data**: Entity Framework models and database context using SQLite
- **Octans.Client**: Blazor Server UI with MudBlazor components
- **Octans.Tests**: xUnit test project

### Key Architecture Patterns
- Clean architecture with dependency injection throughout
- Blazor Server components with viewmodel pattern
- Background services for import folders, downloads, subscriptions, and thumbnail creation
- Refit for API client generation (IOctansApi interface)
- Channel-based queuing for background processing
- Entity Framework Core with SQLite for data persistence

### Major Components
- **Query System**: Tag-based search with complex boolean logic (see docs/Querying.md)
- **Import System**: File importing with hash deduplication and metadata extraction
- **Download System**: Bandwidth-limited downloading with Lua script extensibility
- **Tag System**: Namespaced tags with parent/sibling relationships
- **Thumbnail System**: Background thumbnail generation using ImageSharp

## Code Style
- Use C# 12 features (primary constructors, collection expressions)
- Prefer `var` over explicit types
- Use early returns and guard clauses
- Use `System.Thread.Lock` for lock objects (not regular objects)
- Inject `TimeProvider` for time-based logic
- Inject `IFileSystem` from System.IO.Abstractions for filesystem operations
- Use structured logging: `logger.LogInformation("Message: {Value}", value)`
- Nullable reference types are enabled - handle nulls appropriately
- Use async/await consistently
- Avoid reflection. Prefer to give up on a task than hack at it with reflection

## Test Conventions
- Use xUnit with FluentAssertions and NSubstitute
- Name SUT as `_sut` (field) or `sut` (local)
- Use test helpers instead of mocks for common dependencies

### When to Add Tests
- New public methods/classes in Octans.Core require unit tests
- Bug fixes should include a regression test
- Changes to query parsing require tests in `Octans.Tests/Querying/`
- Changes to import logic require tests in `Octans.Tests/Importing/`
- Changes to download logic require tests in `Octans.Tests/Downloads/`

### Test Helpers (use instead of mocking)
- `MockFileSystem` for filesystem operations
- `FakeTimeProvider` for time-dependent code
- `NullLogger<T>.Instance` for logging
- `DatabaseFixture` for in-memory SQLite
- `SpyChannelWriter<T>` for channel-based assertions

### Test File Location
Tests mirror the source structure:
- `Octans.Core/Importing/Importer.cs` → `Octans.Tests/Importing/ImporterTests.cs`
- `Octans.Core/Downloads/DownloadService.cs` → `Octans.Tests/Downloads/DownloadServiceTests.cs`