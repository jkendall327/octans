# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Octans Development Guide

## Commands
- Build: `dotnet build`
- Run tests: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=Octans.Tests.YourTestNamespace.YourTestClass.YourTestMethod"`
- Run client: `dotnet run --project Octans.Client`

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