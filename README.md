# Octans

Octans is a WIP image management system which uses tags for organisation.

Not ready for serious use yet, but making it public because I have no reason not to.

It's essentially my reimplementation of [the Hydrus Network](https://hydrusnetwork.github.io/hydrus/index.html) in C#.
I'm developing it because:

- I think I could make Hydrus much faster by using C# instead of Python
- I think I could design a more maintainable codebase than Hydrus
- Hydrus is lacking features I want and is unlikely to add them
- It'd be fun (always the most important reason)

## Features

- Local and remote image import and management
- Tag-based organization system with namespaces
- Support for tag relationships (siblings/parents) [prospective]
- Lua-based extensibility to work with custom sites [WIP]

## Getting Started

1. Clone the repository
2. Navigate to the project directory
3. Run the application:
   ```bash
   dotnet run --project HydrusReplacement.Server
   ```

## Project Structure & Getting Started

- `Octans.Server`: lower-level API project that manages the database, filesystem etc.
- `Octans.Client`: User interface in Razor pages, rather janky

Run `dotnet test` on the `Octans.Tests` project to run automated tests.

## Tech used

- Razor pages
- EF Core w/ SQLite
- SixLabors.ImageSharp for image processing
- Lua for extensible downloaders

## License

MIT license.