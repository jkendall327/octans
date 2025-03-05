# Octans

![Logo for Octans, showing a constellation and the project name](docs/octans-logo.svg "Title")

> There is no real mythology related to Octans, partially due to its faintness and relative recentness, but mostly because of its extreme southerly latitude.

—[Wikipedia](https://en.wikipedia.org/wiki/Octans)

Octans is a WIP tag-based image management system.

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

## Project structure & getting started

- `Octans.Server`: lower-level API project that manages the database, filesystem etc.
- `Octans.Client`: User interface in Razor pages, rather janky

Run `dotnet test` on the `Octans.Tests` project to run automated tests.

   ```bash
   dotnet run --project HydrusReplacement.Server
   dotnet run --project HydrusReplacement.Client
   ```

## Tech used

- Razor pages
- EF Core w/ SQLite
- SixLabors.ImageSharp for image processing
- Lua for extensible downloaders

## License

MIT license.