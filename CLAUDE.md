# Octans Development Guide

## Commands
- Build: `dotnet build`
- Run tests: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=Octans.Tests.YourTestNamespace.YourTestClass.YourTestMethod"`
- Run server: `dotnet run --project Octans.Server`
- Run client: `dotnet run --project Octans.Client`

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

## Test Conventions
- Use xUnit with FluentAssertions and NSubstitute
- Name SUT as `_sut` (field) or `sut` (local)
- Use test helpers instead of mocks for common dependencies