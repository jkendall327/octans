# API-backed UI migration

The REST API plus `IOctansClient` is the intended application contract for durable user workflows.
Blazor viewmodels should keep UI-only state such as selection, dialog state, transient progress text,
and browser/session storage. Workflows that should be externally testable should move behind API
endpoints and then through `IOctansClient`.

## Migrated

- Duplicate review: `DuplicateManagerViewmodel` uses `IOctansClient` for scanning, listing candidates,
  and resolving candidates. It no longer injects `ServerDbContext` or `DuplicateService`.
- Gallery search and repository transitions run through `IOctansClient`.
- Details pane tag/note reads, note mutation, and repository transitions run through `IOctansClient`.
- Query builder suggestions run through `IOctansClient`.
- Local file import still writes uploaded files under the UI host, which is a UI-host concern until there is
  a real upload endpoint. Durable import job creation runs through `IOctansClient`.
- Raw URL import job creation runs through `IOctansClient`.
- Import job listing and lifecycle actions run through `IOctansClient`.
- Subscription listing, creation, and deletion run through `IOctansClient`.
- Download status reads run through `IOctansClient`.
- Downloader listing and rescans run through `IOctansClient`.

## Remaining direct Core usage in `Octans.Client`

- `ServiceCollectionExtensions` still wires `ServerDbContext`, startup migration, and health checks. That is
  host infrastructure rather than ordinary UI workflow behavior.
