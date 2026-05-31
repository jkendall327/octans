# API-backed UI migration

The REST API plus `IOctansClient` is the intended application contract for durable user workflows.
Blazor viewmodels should keep UI-only state such as selection, dialog state, transient progress text,
and browser/session storage. Workflows that should be externally testable should move behind API
endpoints and then through `IOctansClient`.

## Migrated

- Duplicate review: `DuplicateManagerViewmodel` uses `IOctansClient` for scanning, listing candidates,
  and resolving candidates. It no longer injects `ServerDbContext` or `DuplicateService`.

## Remaining direct Core usage in `Octans.Client`

These are not intended to be a public app contract. They are UI-host migration debt for issue #210 unless
called out otherwise.

- `GalleryViewmodel` still queries through `IQueryService` and queues repository transitions through the
  in-process repository channel.
- `DetailsPaneViewmodel` still reads tags/notes and queues archive/inbox transitions through Core services.
- `QueryBuilderViewmodel` still calls `QuerySuggestionFinder` directly.
- `LocalFileImportViewmodel` still writes uploaded files under the UI host before creating import jobs.
  The file write is a UI-host concern until there is a real upload endpoint; the job creation should still
  move through `IOctansClient`.
- `RawUrlImportViewmodel` still creates import jobs through `IImportJobService`.
- `ImportJobsPanel` still reads and mutates import jobs through `IImportJobService`.
- `SubscriptionsViewmodel` still lists/adds/deletes subscriptions through `ISubscriptionService`.
- `DownloadsViewmodel` still reads in-process download state through `IDownloadStateService`. The current
  value is live UI refresh from host-local notifications; durable download reads should move through
  `IOctansClient`.
- `ServiceCollectionExtensions` still wires `ServerDbContext`, startup migration, and health checks. That is
  host infrastructure rather than ordinary UI workflow behavior.
