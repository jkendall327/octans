# Architectural decisions

This file lists some design choices about the app. Basically ADRs but made retrospectively.

## Technologies

### SQLite

Using this because it's a single-user app and minimal complexity for setup.
Also makes integration testing trivial since it can run in-memory.

### .NET/C# in general

Very familiar with it, highly performant compared to Hydrus's Python.

### EF Core

Just too convenient to give up.
Open to introducing Dapper or whatever if we need raw SQL for tricky performance moments.

### Blazor

Needed something quick and familiar for a UI. Not wedded to this.

### Lua

I want some kind of scripting capability for extensibility.
- Users can create downloaders for arbitrary sites.
- Users can create custom scripts for UI buttons.

I chose Lua because it seems pretty standard for this, but I don't care particularly.

### Mediator

I want low-level subsystems to be able to report progress, notifications etc. up to higher layers.
This is so the UI (for instance) can display modals - '34/82 images imported'.
I chose Mediator as it's source-generated and in-memory.
But it's probably not what I'm going to stick with forever.
Been considering SignalR, websockets, SSE.

## Background work

This is a big app that does a lot of heavy work - file importing, image manipulation, hashing.
We can't block the user on that stuff, so we have to shunt it into background jobs.

Obviously this adds its own complexity but it's necessary.
Right now it's mainly using in-memory channels and background services, plus the outbox pattern in some places.
That's fine for now since it's a single-user local app.

In general, we want the app to be highly concurrent, in the sense that fifteen different subsystems should be able to work without blocking each other pointlessly.

The database, as always, is the source of truth and we rely on its atomicity guarantees.

## Hashes and buckets

The purpose of the app is to manage access to a lot of images on the filesystem.

The way Hydrus does this is to carve out a little domain of its own and establish buckets bashed on content hashes.

You hash an item, take the first two characters and stuff it into an according folder.

It's basically a hash map as reified via the filesystem.