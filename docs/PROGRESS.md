# Octans Progress Map

This is a plain-English map of the major pieces of Octans.

## How to Use This Document

Each section should eventually answer:

- What the subsystem is supposed to do.
- What currently works well enough to use.
- What is present but awkward, partial, or risky.
- What is missing entirely.

The question to answer is, 'what needs to be done before this can be hooked up to everything else'?
And then, 'what needs to be done to make it usable day-to-day'?
And then, 'what needs to be done to make it fully robust long-term'?

These descriptions will likely end up becoming GitHub issues.

## Backend Product Surface

The backend-facing shape of the app as a product: the REST-ish API, service boundaries, DTOs, client-independent workflows, and the line between reusable backend behavior and the current Blazor UI.

## Content Storage and Filesystem Layout

Hash-based storage, original media placement, repository folders, filesystem safety, path handling, cleanup, and deletion from disk.

What exists now:

- The core storage model is content-addressed. `ContentHash` computes SHA-256 hashes, exposes hex/hash bytes, and derives bucket names.
- `ImageStorage` is the central path API for originals, thumbnails, lookup, deletion, and storage initialization.
- Originals live under `AppRoot/db/files/fXX/<HASH>.<extension>`, and thumbnails live under `AppRoot/db/files/tXX/<HASH>.jpeg`.
- `ImageStorage.EnsureStorage` creates all 256 original buckets and all 256 thumbnail buckets at startup, plus the `downloaders` directory.
- Imports detect media extension/content type through `ImageStorage.GetMetadata`, store originals through `FilesystemWriter`, persist `HashItem.Hash`, `Extension`, `ContentType`, and repository state through `DatabaseWriter`, and enqueue thumbnail creation.
- New media rows default to Inbox unless import requests set `AutoArchive`, in which case they are stored as Archive.
- `/media/{hash}` is the stable media-serving route. It parses the hash, looks up DB metadata, finds the original through `ImageStorage`, sets long immutable cache headers, uses the content hash as the ETag, and enables range processing.
- `OctansClient.GetMediaUrl` and the gallery paths build URLs around `/media/{HASH}` rather than exposing physical storage paths.
- `FileDeleter` removes the original and thumbnail, then soft-deletes the `HashItem` by setting `DeletedAt`.
- Reimport handling can reactivate a deleted hash and restore the original file if the DB row exists but the bytes have been removed from disk.
- Repository movement is DB-backed: Inbox/Archive/Trash are represented by `RepositoryId`, and the repository change background service applies queued changes by hash.
- `StorageService` can compute rough app-root disk usage for the home stats surface.
- Tests cover hash behavior, deterministic original lookup, fallback lookup by hash in a bucket, deletion from disk plus DB soft delete, storage size formatting, import metadata persistence, thumbnail queueing, and reimport restoration.
- Durable storage-maintenance jobs scan the database and content buckets in the background while imports and downloads are idle. Findings and repair outcomes survive restarts and are exposed through the API and storage-health dashboard.
- Storage scans detect missing, orphaned, malformed, misplaced, duplicate, and hash-mismatched files as well as missing thumbnails and inconsistent media metadata.
- Confirmed repair jobs regenerate thumbnails, correct metadata and deterministic placement, and quarantine unsafe files without silently deleting original bytes.

Important current limitations:

- File writes and database writes are not atomic as one operation. `ImportItemProcessor` writes bytes first and then saves the DB row, so a failed DB save can leave orphaned files; the reverse class of DB/file mismatch is also possible after crashes.
- Reconciliation is periodic rather than a blocking startup gate. A newly started app can therefore serve requests before its first automatic scan has completed.
- `HashItem.Hash` is not protected by a database unique index in the current model snapshot, so duplicate rows for the same content hash are still possible under concurrent imports.
- `ImageStorage.FindOriginal` can fall back to scanning a bucket when extension metadata is missing. That helps old rows, but it keeps a slower legacy path alive and can hide metadata gaps.
- The system still calls this `ImageStorage`, even though the product language says media and the importer may eventually support videos, archives, and other file types.
- Thumbnail storage assumes JPEG thumbnails and image-decodable content. Non-image media types do not have a clear derivative strategy.
- `FileRecord` exists in the schema but is effectively unused, which makes the intended relationship between imported source paths, stored hashes, and physical files unclear.
- `/approot/{**path}` exposes configured app-root files outside the content-addressed media route. That may be useful for local development/debugging, but it is a broad filesystem-serving surface and bypasses the `ImageStorage` boundary.
- Deletion is soft in the database but destructive on disk. Trash semantics therefore mean "metadata row remains, original bytes may be gone", not a recoverable trash folder. // this is intended
- There is no user-facing or API-level media metadata contract yet. Public file/search endpoints still expose raw `HashItem` rows rather than stable DTOs with hash, media URL, content type, size, dimensions, and repository state.
- Disk usage is a recursive app-root total, not a storage subsystem view. It does not separate originals, thumbnails, database, download staging, logs, or orphaned data.

Before this can be hooked up to everything else:

- Decide the durable media abstraction: whether this remains image-specific or becomes a `MediaStorage`/`ContentStorage` boundary with originals, thumbnails/previews, and future derivatives.
- Make the import write path crash-tolerant. Use deterministic staging beside the final destination, validate bytes/metadata, then move into place and persist DB state in a deliberate order with cleanup for failed attempts.
- Add DB constraints/indexes for storage invariants, especially unique content hashes and repository FK assumptions.
- Extend reconciliation policy for non-image derivatives and any future storage layout versions.
- Replace or clearly scope the broad `/approot` file-serving route. The normal frontend should use `/media/{hash}` or explicit safe endpoints, not arbitrary app-root paths.
- Define stable media DTOs and endpoints: get media metadata by hash/id, get original URL, get thumbnail/preview URL, list derivatives, and expose repository/deleted state without leaking EF row shapes.
- Decide the recoverability semantics for Trash. If deletion from disk is intentional, make the UI/API honest; if recovery is desired, trash needs to keep bytes or move them through a separate lifecycle.
- Establish how non-image media should be stored and presented. Original storage is already generic enough, but metadata detection, thumbnails, content-type validation, and UI routes need explicit behavior.

For day-to-day usability:

- Show useful media metadata: content type, extension, byte size, dimensions for images, import date, repository, deletion state, and whether the original/thumbnail exists on disk.
- Add one-file and force-regenerate-all thumbnail controls alongside the existing missing-thumbnail repair.
- Make storage usage actionable: break down originals, thumbnails, database, download staging, and other app-root data.
- Provide clear messages when `/media/{hash}` cannot serve a file because the hash is invalid, the DB row is missing, or the physical file is missing.
- Keep import/reimport messages precise. An already-present file, a deleted-and-restored file, and a missing-original-restored file should not all look like the same outcome.

For long-term robustness:

- Add retention and review tools for quarantined content after users have had an opportunity to inspect it.
- Consider recording original byte size, dimensions, detected content type, extension, created/imported timestamps, and maybe a storage version on `HashItem` or a related media metadata table.
- Make derivative generation extensible: thumbnails for images, poster frames for video, icons/previews for other media, and explicit failure records for media that cannot produce derivatives.
- Add large-library tests or benchmarks for bucket lookup, `/media/{hash}` serving, thumbnail regeneration, deletion batches, and reconciliation across many files.
- Decide whether content should ever be migrated between layouts. If so, store enough versioning to support future bucket-layout changes safely.
- Add backup/restore documentation and tests for the app-root shape: SQLite database, content buckets, thumbnails, downloader scripts, settings, download staging, and any future derivative folders.
- Harden path safety around every user-influenced filesystem path, especially local imports, downloader scripts, app-root static serving, and any future export/backup paths.
- Add observability around storage operations: bytes written/deleted, missing-file failures, thumbnail failures, reconciliation results, and media-serving errors.

## Importing

Local file imports, URL imports, raw byte providers, import jobs, import filters, tag assignment during import, reimport handling, delete-after-import behavior, and job control.

## HTTP Download Complex

The HTTP download complex is the feature-agnostic transfer subsystem. Other features should be able to submit HTTP work, let the shared machinery handle transfer policy, and then consume explicit results without each feature reinventing retries, host limits, progress, validation, or file safety.

What exists now:

- There is a durable download job model backed by `DownloadStatus` and `QueuedDownload`.
- `IDownloadService` is the feature-facing entry point for queueing jobs, getting typed handles/results, and issuing cancel/pause/resume/retry commands.
- The subsystem is split into clearer pieces: lifecycle service, state tracker, durable queue, active cancellation registry, background worker, HTTP streamer, completion notifier, job-result reader, request-header provider, host-circuit registry, bandwidth gate, disk-space guard, and staging-path helper.
- The background worker drains the durable queue with global concurrency and worker-level per-domain concurrency.
- Queue ordering supports priority and queued time.
- Pause/resume semantics are explicit: active transfers are stopped and later re-queued from the beginning. Byte-level HTTP range resume is intentionally out of scope for now.
- Startup recovery restores queued/in-progress work and deletes stale deterministic `.part` files.
- Downloads stream through `HttpCompletionOption.ResponseHeadersRead` into deterministic staging files beside the destination, then move into place only after validation succeeds.
- The streaming loop reports progress and speed, uses byte-aware global/per-domain bandwidth pacing, checks disk space, and enforces max-size limits both before and during transfer.
- Content-Length mismatch, expected SHA-256/SHA-384/SHA-512 hash mismatch, content-type mismatch, missing credentials, disk-space failure, size-limit failure, HTTP terminal failure, cancellation, and generic network/filesystem failures have distinct paths.
- Terminal result metadata is persisted and exposed through `DownloadJobResult`: outcome, failure category, status code, content type, ETag, Last-Modified, validation message, bytes, destination, source type/id, and timestamps.
- The named `DownloadClient` uses `Microsoft.Extensions.Http.Resilience` with exponential retry, jitter, and per-host circuit-breaker pipelines.
- Host circuits feed back into queue selection so an unhealthy host does not block healthy hosts.
- Request headers support default User-Agent, per-domain overrides, auth/cookie headers, required-header checks, wildcard domain matching, and a request fingerprint.
- Download timeouts are layered: connection establishment, response headers, overall job duration, and idle/stall body reads are configured separately, with timeout failures distinct from user cancellation.
- There is a real integration harness for the download manager using actual DI, queue/state/background/downloader services with the bottom HTTP edge faked.
- Tests cover queue ordering/restoration, state transitions, pause/cancel/retry, terminal notifications, staging cleanup, HTTP failures, transient retry, host circuits, per-domain concurrency, bandwidth pacing, disk-space checks, content-type validation, size limits, hash validation, request headers, credential failures, response-header timeout, idle/stall timeout, and cancellation/timeout distinction.

Known GitHub backlog:

- #180: deduplicate in-flight requests for the same URL.
- #183: make per-host connection pooling and concurrency limits explicit below the worker-level cap.
- #185: support conditional re-fetching with ETag/Last-Modified and `304 Not Modified`.
- #187: add structured metrics, not just structured logs.
- #193: tracker for the hardening work; most child issues are closed, with the above still open.
- #202: route `RawUrl` imports through the download-job subsystem instead of `SimpleImporter.GetByteArrayAsync`.
- #203: move downloader discovery fetches behind a shared HTTP document/body-fetch abstraction.

Important current limitations:

- The shared subsystem is strong internally, but only partially integrated into product workflows. `PostImporter` queues downloads, but `RawUrl` imports still bypass it and download full remote files into memory.
- `DownloaderService` uses the named `DownloadClient` and shared header provider, but it still performs direct external HTTP document fetches instead of using a dedicated `Octans.Core/Http` abstraction.
- The durable queue does not deduplicate identical in-flight requests yet. `RequestFingerprint` exists, but there is no coalescing model or multi-subscriber completion model.
- Per-domain concurrency is enforced by the background worker, not by configured lower-level connection pooling or redirect-aware host accounting.
- Conditional requests are not implemented. Response ETag/Last-Modified are persisted, but there is no first-class re-fetch request path or `NotModified` handling.
- Redirect policy is mostly whatever the HTTP client/resilience stack does by default. There is no explicit redirect cap/loop/cross-domain policy in the download subsystem.
- Observability is mostly logs and status rows. There is no metrics surface for per-host queue depth, active downloads, latency, bytes, retry counts, failure rates, or circuit state.
- Feature-level continuation is still loose. A caller can poll results or provide an `IDownloadCompletionNotifier`, but there is not yet a polished bridge for workflows like "download then import this completed artifact".

Before this can be hooked up to everything else:

- Build the import/download bridge for `RawUrl` imports (#202). The user-facing import job should remain the workflow owner, but the HTTP transfer should go through the durable download subsystem.
- Add a bounded HTTP document/body fetch abstraction under `Octans.Core/Http` for downloader discovery pages (#203). This should share the named client, headers, host policy, cancellation, response-size limits, failure handling, and logging without pretending every HTML/API discovery fetch is a durable file download.
- Define the feature-facing completion pattern more concretely. Polling typed results, completion notifier hooks, and import handoff need one clear application-level story.
- Expose download operations through a frontend-neutral API: queue job, list jobs, inspect job, pause, resume, cancel, retry, and fetch terminal result.
- Decide which `DownloadRequest` fields are part of the stable API contract: provenance, priority, allowed content types, expected hashes, destination, headers/auth identity, and future conditional validators.
- Implement in-flight dedupe semantics (#180), including the hard part: same URL with different destinations, headers/auth, cancellation ownership, and terminal results for every interested caller.
- Make host identity consistent across concurrency, circuit breakers, redirects, credentials, metrics, and dedupe.
- Add layered timeout policy (#188) before relying on this for arbitrary external sites.

For day-to-day usability:

- Provide a user-visible download/history surface that is not tied to Blazor internals: queued, active, paused, failed, completed, retryable, and validation-failed downloads.
- Make failure messages actionable. A user should be able to tell the difference between "host is down", "not authorized", "HTML returned instead of image", "file too large", "disk full", "hash mismatch", and "stalled".
- Add metrics or at least a queryable health snapshot for queue depth, active downloads, bytes/sec, per-host failures, open circuits, retries, and validation failures (#187).
- Add domain/source configuration that users can realistically edit: bandwidth, concurrency, size caps, content-type strictness, User-Agent, auth/cookies, and required credentials.
- Support conditional re-fetches (#185) for subscriptions and refresh flows so Octans can avoid re-downloading unchanged resources.
- Make retry/backoff/circuit behavior visible enough that users know whether a host is being cooled down or retried.
- Add retry controls that match the result model: retry failed/canceled jobs, maybe retry all failed jobs for a source/domain, and avoid retrying terminal failures blindly.
- Keep pause/resume wording honest until byte-range resume exists. "Resume" means re-queue from the beginning right now.

For long-term robustness:

- Decide whether true byte-range resume is worth implementing. If it is, it needs ETag/Last-Modified validators, range requests, partial-file validation, and careful interaction with staging files. Until then, keep it explicitly out of scope.
- Make redirect behavior explicit: cap redirects, detect loops, decide cross-domain policy, and avoid leaking sensitive headers across hosts.
- Add lower-level connection pooling limits and document how they differ from worker concurrency and bandwidth throttling (#183).
- Build a durable or queryable metrics story, preferably compatible with `System.Diagnostics.Metrics` or OpenTelemetry.
- Make host/resource policy composable across durable downloads and smaller discovery/document fetches.
- Consider whether state restoration should reconcile old terminal rows, stale queued rows, missing destinations, and completed files more deeply than today's active-download restoration.
- Add large-library/load testing around many queued jobs, many domains, failing hosts, slow hosts, and long-running downloads.
- Add security hardening around credentials: storage location, redaction, domain scoping, redirect behavior, and diagnostics.
- Keep the subsystem feature-agnostic. Importing, subscriptions, and Lua downloaders should call into this boundary; they should not push feature-specific logic down into `HttpDownloader`.

## Downloaders

User-created website integration scripts: Lua downloader definitions, metadata, parser contracts, script execution, downloader discovery, and how downloader output feeds imports or downloads.

## Subscriptions

Recurring scans of web sources: subscription definitions, scheduling, provider execution, status reporting, deduplication of discovered content, and failure/retry behavior.

What exists now:

- There is a persisted subscription model: `Provider`, `Subscription`, and `SubscriptionExecution`.
- A subscription stores a name, provider/downloader, query string, check period, next-check timestamp, and a history of execution rows with executed-at time and item count.
- `SubscriptionService` can add, list, delete, and periodically execute due subscriptions.
- `SubscriptionBackgroundService` runs every minute and calls `SubscriptionService.CheckAndExecute`.
- The Blazor subscriptions page can list subscriptions, show last run/items found/next check, and create or delete rows through a dialog.
- The add-subscription dialog discovers downloader names from `DownloaderFactory`, so the UI already assumes subscriptions are tied to downloader definitions.
- The executor boundary exists as `ISubscriptionExecutor`, returning `SubscriptionExecutionResult`.
- Basic tests cover adding/listing/deleting subscriptions through the viewmodel and persisting an execution result.

Important current limitations:

- The registered executor is `NoOpSubscriptionExecutor`, so subscriptions do not currently scan any website or produce import/download work.
- `SubscriptionExecutionResult` only records `ItemsFound`; it cannot represent discovered URLs, queued downloads/imports, failures, skipped duplicates, pagination state, or useful diagnostics.
- The scheduler executes all due subscriptions sequentially inside one scoped service loop. There is no per-subscription locking, concurrency limit, cancellation model, manual run command, or protection against overlapping runs after process restarts.
- Failure handling is too blunt. If one subscription executor throws, the whole check loop can stop before saving successful execution history or rescheduling later subscriptions.
- `NextCheck` is advanced from the scheduler's initial `now`, not from actual completion time, and there is no failed-run/backoff policy.
- The data model does not have an enabled/paused flag, last-success/last-failure fields, error messages, consecutive failure counts, stable cursor/checkpoint data, or per-subscription options.
- `Provider` currently only has a name and is created on demand from the downloader name. It does not encode source type, base URL, credentials, policy, display metadata, or whether the referenced downloader still exists.
- The API surface is effectively absent. `POST /subscriptions` still throws `NotImplementedException`, `SubscriptionRequest` is unused and incomplete, and there are no frontend-neutral endpoints for listing, creating, updating, deleting, pausing, or running subscriptions.
- The existing UI is an admin table for rows, not a usable subscription workflow. It has no edit/enable/manual-run controls, no run history detail, no failure visibility, no import/download links, and no status refresh after background execution.
- There is no deduplication or "already seen" model at the subscription layer. The app can avoid duplicate file imports later by hash/reimport logic, but subscriptions cannot yet remember which source URLs or posts they have already processed.
- The downloader scripting surface has a `process_query` hook and gallery URL generation, but the subscription code does not use either. `DownloaderService.ResolveAsync` only resolves a concrete URL to downloadable URLs.
- There is no clear bridge from a subscription scan to durable import jobs or durable download jobs. `PostImporter` can queue downloads for resolved post URLs, but it is not connected to subscriptions, and queued downloads are not yet imported after completion.
- Subscription-generated HTTP work has not been placed behind the shared HTTP document/body-fetch abstraction called out in the download backlog.

Before this can be hooked up to everything else:

- Decide the subscription input model. A subscription probably needs at least downloader/provider, query or seed URL, frequency, tags to apply, import destination behavior, archive/inbox behavior, and maybe per-source HTTP policy.
- Define the executor contract around real work, not just `ItemsFound`. It should return discovered source items, skipped/duplicate items, queued import/download handles, failure details, and any next-page/checkpoint information that must persist.
- Implement a real downloader-backed executor. The first useful version can run downloader query/gallery logic, resolve discovered post/media URLs, and hand off to import/download jobs without trying to solve every website pattern.
- Make subscriptions submit durable work rather than doing fragile in-memory work. The natural direction is: subscription execution discovers candidate sources, creates import jobs or download jobs with `SourceType = "Subscription"` and a stable source ID, then records what was queued.
- Build the workflow continuation for "download then import" before subscriptions rely on raw queued downloads. Otherwise subscriptions can queue files into temporary paths without making them part of the library.
- Add an "already seen" or source-item table before running subscriptions repeatedly. It should key on stable source identity/request fingerprint/post URL, not just final content hash, so the system can skip known posts before downloading them again.
- Harden scheduling semantics: enabled/disabled state, manual run, per-subscription in-progress tracking, no overlapping executions, failed-run recording, retry/backoff, and continued processing when one subscription fails.
- Replace the placeholder endpoint with a real API: list subscriptions, create/update/delete, enable/disable, run now, list executions, and inspect any import/download jobs created by an execution.
- Decide whether subscription configuration should reference downloader names directly or stable downloader IDs/versions. Names are convenient but brittle if downloader files are renamed or upgraded.
- Integrate with the shared HTTP policy layer for discovery pages and API calls. Subscription scans should share request headers, credentials, host limits, response caps, cancellation, and logging with downloader discovery fetches.

For day-to-day usability:

- Provide a subscription page that answers "what is this doing right now?": enabled state, next run, last success, last failure, current run status, items found, items queued, items imported, and useful error text.
- Add edit, pause/enable, run-now, and delete controls, with confirmation when deleting history or queued work.
- Show execution history with links to the import jobs/download jobs created by each run.
- Let users attach tags and import options to a subscription so recurring imports land with useful metadata instead of becoming anonymous downloads.
- Make duplicate/skipped behavior visible. Users should know whether a run found nothing, found only already-seen posts, queued new work, or failed before it could scan.
- Add validation for bad downloader names, missing downloader capabilities, invalid query/URL input, too-small frequencies, and deleted or broken downloader scripts.
- Make failure messages distinguish downloader-script contract errors, HTTP failures, auth/credential failures, parser failures, no-results runs, and import/download handoff failures.
- Add per-subscription or per-provider policy knobs where they matter: frequency, retry/backoff, max pages/items per run, content-type expectations, size caps, credentials, and host throttling.

For long-term robustness:

- Treat subscription runs as durable jobs with phases, not just execution log rows. Discovery, queueing, downloading, importing, and final reconciliation each need observable state.
- Add checkpoint/cursor support for paginated APIs and galleries so subscriptions can resume or continue without rescanning the entire source every time.
- Build a source-item/history model that can handle post URL changes, multiple media per post, changed remote files, deleted remote posts, redirects, and conditional re-fetches.
- Use conditional requests where possible once the HTTP layer supports `ETag`/`Last-Modified` re-fetches.
- Make concurrency and fairness explicit across many subscriptions: per-provider limits, global subscription worker limits, queue priorities, and backpressure from the download/import systems.
- Add structured metrics for subscription run counts, durations, discovered items, queued items, imported items, skips, failures, and per-provider health.
- Add end-to-end integration coverage with fake downloader scripts, fake HTTP pages/API responses, durable import/download jobs, repeated runs, failures, restarts, and duplicate source items.
- Decide the security model for downloader scripts and credentials. Subscriptions will run scripts unattended, so credential scoping, redaction, timeouts, and script failure isolation matter more than in manual one-off imports.

## Tags and Tag Relationships

Tags are the core metadata language of the app. This subsystem covers the tag tables themselves, assigning tags to files, resolving tag aliases, expanding tag parent relationships, suggesting tags while searching, and exposing all of that through a frontend-agnostic API.

What exists now:

- The core schema exists: namespaces, subtags, namespace/subtag tag pairs, hash-to-tag mappings, tag parents, and tag siblings.
- Importing can persist tags supplied with imported files, including durable import jobs that serialize per-source tags.
- Existing files can have tags added or removed through `TagUpdater`, exposed as `POST /tags`.
- `ITagService.GetTagsForHashAsync` can read the tags for a content hash, and the gallery details/image viewer paths use it.
- Querying searches direct tag mappings, has some namespace wildcard support at the lower search layer, and expands included parent tags to descendant tag IDs.
- Query autocomplete exists through `QuerySuggestionFinder`, but it is currently an in-process Blazor/viewmodel dependency rather than a backend API contract.
- Parent relationships have basic add/remove/descendant operations and cycle prevention.
- Sibling relationships have a resolver that can map non-ideal tags to ideal display tags, but not a management workflow.
- Tests cover basic add/remove, importing with tags, tag splitting, autocomplete, parent traversal/cycle checks, and sibling display resolution.

Before this can be hooked up to everything else:

- Define the backend tag API properly. The app probably needs endpoints for listing/searching tags, getting tags for a file/hash, updating tags, autocomplete, parent relationship CRUD, sibling relationship CRUD, and tag detail/count views. The current `POST /tags` endpoint only covers add/remove by database hash ID.
- Pick stable identifiers for tag operations. Some paths use database hash IDs, some use content-hash hex strings, and future API clients will need a consistent way to address files and tags.
- Centralize tag creation and lookup. `DatabaseWriter`, `TagUpdater`, and `TagParentService` each have their own get-or-create logic for namespaces, subtags, and tags.
- Decide the exact semantics of parents and siblings. In particular: whether imports canonicalize sibling tags, whether search matches both ideal and non-ideal tags, whether parent tags are materialized or query-derived, and whether displayed tags should show implied parents.
- Wire relationships into the normal flows. Parent expansion is partly used by search; siblings are not meaningfully wired into import, tag assignment, tag display, or search.
- Expose autocomplete and tag discovery through the API rather than only through Blazor view models.
- Decide what the `Status` fields on tag parent/sibling rows are for, or remove them with a migration.

For day-to-day usability:

- Add tag management workflows: create, rename, merge, delete, bulk add/remove, and clean up unused tags.
- Add relationship management workflows: create/remove parents, create/remove siblings, inspect children/parents, inspect aliases, and explain why a tag appears or why a query matched.
- Make tag editing ergonomic for imports and existing files: common tags, recent tags, batch edits, paste/import of many tags, and clear feedback when an operation creates new tags.
- Establish normalization rules for whitespace, casing, empty namespaces, namespace delimiters, wildcard characters, and invalid input.
- Return useful errors for bad tags, unknown files, duplicate/conflicting relationships, and invalid relationship cycles.
- Provide counts and previews: how many files have a tag, which files would be affected by a rename/merge/delete, and which aliases/parents are attached.
- Make the API response shapes frontend-neutral. A future React client should not need to know EF row shapes or Blazor viewmodel conventions.

For long-term robustness:

- Add database constraints and indexes for the invariants the code already assumes: unique namespace values, unique subtag values, unique namespace/subtag tag pairs, unique hash/tag mappings, unique parent pairs, and unique sibling pairs.
- Make get-or-create operations concurrency-safe. Right now duplicate prevention is mostly application logic, which is fragile if imports or tag edits run concurrently.
- Complete relationship invariants. Parent cycles are checked, but sibling chains, sibling cycles, conflicting ideals, parent/sibling interactions, and relationship deletion behavior need explicit rules.
- Make query semantics complete and testable for tags: wildcard include/exclude behavior, exclude-only queries, parent expansion, sibling resolution, OR behavior, and large-library performance.
- Decide whether derived relationship behavior should be computed live, cached, or materialized. Parent expansion by loading all relationships into memory is fine for small data, but it needs a better long-term plan for Hydrus-sized tag graphs.
- Add migration/backfill tools for future normalization, aliasing, and relationship changes.
- Expand integration coverage around the full path: import with tags, update tags through the API, query by direct tag, query by parent, resolve sibling display/search behavior, and verify the API shapes a non-Blazor frontend would use.

## Querying and Search

Querying is the path from user search text to matching media. This subsystem covers the query grammar, parser, planner, SQL execution, result pagination, repository filters, tag relationship expansion, autocomplete, and the API contract a future frontend should use.

What exists now:

- There is a documented intended query language in `docs/Querying.md`: tag predicates, negation, wildcards, OR, nested OR, and system predicates.
- The code has a recognizable pipeline: `QueryParser` turns raw strings into predicates, `QueryPlanner` wraps/caches a plan, `QueryTagConverter` reduces the plan into search inputs, and `HashSearcher` executes against EF/SQLite.
- `QueryService` exposes count and query operations for in-process consumers.
- The gallery viewmodel uses `IQueryService.CountAsync` and `IQueryService.Query` to populate image URLs and show progress.
- The HTTP API exposes `POST /files/query`, and `OctansClient.QueryFilesAsync` calls it.
- Empty queries and `system:everything` return all non-trash files.
- `system:inbox`, `system:archive`, and `system:trash` exist as repository filters.
- Exact tag search works at the execution layer.
- Query execution defaults to excluding trash unless the query asks for trash explicitly.
- `HashSearcher` has lower-level support for limit/offset and count, though the public API does not expose pagination yet.
- Query suggestions exist through `QuerySuggestionFinder` and are used by the Blazor query builder.
- Tests cover parser basics, some repository filtering, exact tag search, lower-level namespace wildcard execution, count, limit/offset, suggestions, and query-builder suggestion wiring.

Important current limitations:

- The parser recognizes OR, negation, and wildcard syntax, but execution does not faithfully implement those semantics yet.
- `QueryTagConverter` currently ignores OR predicates in the main reduction path, so OR queries are not safe to treat as working end-to-end.
- Wildcard predicates are parsed but mostly discarded by the reduction layer. The searcher has a manual `WildcardNamespacesToInclude` path, but ordinary parsed wildcard queries do not reach it.
- Negation does not behave like "all matching files except these files". It mostly removes excluded tag IDs from the include-ID set, which is not enough for normal NOT semantics.
- Multiple included tags currently behave closer to "has any of these tags" than Hydrus-style "must satisfy every predicate".
- System predicates are limited to everything/inbox/archive/trash. Filesize, dimensions, tag count, import date, media type, rating, duration, and similar predicates are not implemented.
- The API returns EF `HashItem` rows rather than a frontend-neutral search result DTO.
- Autocomplete is in-process only. There is no `/query/suggestions` or equivalent API surface for a non-Blazor frontend.

Before this can be hooked up to everything else:

- Decide and document the real query semantics before building more UI/API on top: AND between normal predicates, OR grouping, NOT behavior, wildcard behavior, parent expansion, sibling resolution, repository filters, and empty query behavior.
- Make parser, reducer, and executor agree. Right now the grammar accepts more than the executor can honestly answer.
- Replace the loose `IEnumerable<string>` API body with a stable query request type that can carry query terms, page size, cursor/offset, sort order, repository scope, and perhaps "include deleted/trash" policy.
- Return a search result DTO rather than raw `HashItem` EF entities. The API should return IDs/hashes/media URLs/metadata that a future frontend can depend on.
- Expose count and pagination through the API. The gallery already wants count for progress, and large libraries cannot rely on returning the whole result set.
- Expose query suggestions through the API.
- Decide how query errors should be represented. Unsupported system predicates currently throw exceptions rather than returning user-actionable parse/validation errors.
- Remove or quarantine stale paths like `FileFinder.GetFilesByTagQuery`, which appears separate from the real query pipeline and has suspicious matching logic.

For day-to-day usability:

- Implement the common search grammar well: exact tags, namespace/subtag wildcards, negation, OR, parent-expanded queries, repository filters, and useful system predicates.
- Add practical system predicates: file size, dimensions, file type/content type, import date, deleted/trash state, tag count, rating, duplicate status, and maybe source/import job.
- Add stable sorting: import date, hash ID, random, file size, dimensions, rating, and maybe "recently viewed" later.
- Add pagination or cursoring that feels good in the gallery and does not require loading every result.
- Improve suggestions so they can suggest tags, namespaces, system predicates, recent searches, and maybe common operators.
- Provide clear user-facing error messages for invalid syntax, unsupported predicates, and queries that are valid but currently unimplemented.
- Add "explain this query" or at least developer-facing diagnostics for why a query matched, especially once parents/siblings are active.
- Preserve search state in a frontend-neutral way so changing the UI does not lose query history, saved searches, or shareable search URLs.

For long-term robustness:

- Build a real query model with explicit predicate types and validated semantics. String parsing should be a thin input layer, not the only place where meaning lives.
- Add end-to-end tests from raw query strings to result sets for every supported grammar feature, including combinations like `character:mario -series:mario_bros system:archive`.
- Add performance-oriented integration tests or benchmarks for large tag/mapping counts.
- Design indexes around the real query plan: mapping by tag, mapping by hash, repository filtering, system predicate columns, and common sort orders.
- Decide whether query planning is worth caching. The current planner cache keys on predicate object hash codes, which is unlikely to be a stable long-term contract.
- Make tag relationship behavior scalable. Parent expansion currently loads all parent relationships into memory; that may be fine for now, but large tag graphs need a deliberate strategy.
- Consider moving complex execution to explicit SQL/CTEs when EF becomes awkward, especially for AND/OR/NOT combinations and parent expansion.
- Add observability for slow queries: query text/shape, elapsed time, result count, database timing, and whether relationship expansion or wildcard matching dominated the work.
- Keep `docs/Querying.md` synchronized with implementation so it is a real spec, not just an aspiration.

## Repositories and File Lifecycle

Inbox/archive/trash semantics, repository membership, repository changes, file deletion, recovery expectations, and how lifecycle transitions affect storage and search.

## Duplicate Processing

Perceptual hashing, duplicate candidate generation, duplicate decisions, duplicate resolutions, and workflows for comparing or merging near-identical media.

What exists now:

- `HashItem` has an optional `PerceptualHash` column.
- Duplicate state is persisted in `DuplicateCandidate` and `DuplicateDecision`.
- `DuplicateCandidate` records a pair of hash IDs, a similarity score in the `Distance` column, and creation time.
- `DuplicateDecision` records a pair of hash IDs, a resolution, and decision time.
- `DuplicateResolution` currently supports `Distinct` and `KeepBoth`.
- `PerceptualHashProvider` wraps `CoenM.ImageHash` and computes a 64-bit perceptual hash from an image stream.
- `DuplicateService.CalculateMissingHashes` processes up to 100 non-deleted hashes with missing perceptual hashes, reads originals through `ImageStorage`, and saves calculated perceptual hashes.
- `DuplicateService.FindDuplicates` loads non-deleted rows with perceptual hashes, builds an in-memory Hamming-distance index, and creates candidates above a 95% similarity threshold.
- Existing candidates and previous decisions are treated as ignored pairs so duplicate scans do not keep recreating the same pair.
- Resolving a candidate with no keep-hash records a `DuplicateDecision` and removes the candidate.
- Resolving a candidate with a keep-hash deletes the other file through `FileDeleter` and removes all candidates involving the deleted hash.
- There is a `/duplicates` Blazor page reachable from the toolbar. It can manually trigger hash calculation plus candidate search, list up to 50 candidates, and resolve visible pairs.
- Tests cover missing perceptual-hash calculation, thresholded candidate creation, skipping existing candidates, respecting decisions, deleting the non-kept file, and rejecting a keep-hash that is not part of the candidate.

Important current limitations:

- Duplicate processing is manual and UI-driven. There is no durable duplicate-scan job, background worker, progress model, cancellation, resumability, or scheduled scan after imports.
- There is no frontend-neutral API for duplicate scans, candidate listing, candidate detail, or resolution.
- The Blazor viewmodel reads `ServerDbContext` directly and builds DTOs itself, so duplicate review is still tied to the current UI/data shape.
- Candidate image URLs are currently built as `/api/files/{hash}`, but the real media route is `/media/{hash}`. The review UI may not render images correctly until that is fixed.
- `DuplicateCandidate.Distance` actually stores a similarity percentage, not a distance. The name is misleading in the DB model and UI boundary.
- The fixed 95% similarity threshold is hard-coded. There is no setting, no per-media-type strategy, and no way for the user to tune noisy or missed candidates.
- Candidate generation loads all perceptually hashed, non-deleted rows into memory and compares through an in-process index. That is fine for a small library, but it is not a complete large-library plan.
- Perceptual hashes are only calculated for files that `CoenM.ImageHash` can decode as images. Non-image media do not have duplicate handling.
- Failed hash calculations are logged but not persisted. A broken/corrupt/unsupported file will be retried on every scan because there is no "hash failed" state.
- There is no stale-candidate reconciliation when files are deleted, restored, moved to trash, reimported, or when perceptual hash logic changes.
- The UI resolution language is muddy. Clicking "Keep This" passes `KeepBoth` plus a keep-hash, and the service deletes the other side while ignoring the resolution value.
- Keep-one deletion records no duplicate decision. It deletes the non-kept file and removes related candidates, but there is no durable record explaining that the pair was resolved by deleting one side.
- Decisions only support "distinct" and "keep both". There is no richer Hydrus-style duplicate relationship vocabulary such as better/worse, same quality, alternate, false positive, or unknown.
- There is no tag/rating/note/repository merge workflow when one duplicate is deleted. Keeping one file can discard metadata attached to the deleted hash.

Before this can be hooked up to everything else:

- Fix the candidate DTO/media URL path so duplicate review uses `/media/{hash}` or a stable media DTO instead of `/api/files/{hash}`.
- Split the duplicate workflow into clear backend operations: calculate missing perceptual hashes, find candidates, list candidates, inspect a candidate, resolve a candidate, and maybe run a full scan job.
- Introduce a durable duplicate-scan job or integrate duplicate scanning into the existing background job patterns. Long scans need progress, cancellation, failure reporting, and safe restart behavior.
- Define the resolution model before building more UI on top. At minimum, separate "not duplicates", "keep both but related", and "delete one side"; ideally decide whether Octans wants Hydrus-like duplicate relationship semantics.
- Record keep-one outcomes durably, including which hash was kept, which was deleted, and why.
- Decide how metadata should be handled when deleting one side: move/merge tags, notes, ratings, repository state, source/import metadata, and future relationship data, or explicitly leave them behind.
- Persist hash-calculation failure state so unsupported/corrupt media do not get retried forever without explanation.
- Add candidate cleanup/reconciliation for deleted hashes, restored hashes, missing originals, and changed perceptual hashes.
- Expose duplicate operations through a frontend-neutral API and DTOs rather than raw EF models or Blazor-only viewmodels.

For day-to-day usability:

- Make duplicate review images actually render, show useful metadata beside each side, and make the keep/delete actions unambiguous.
- Show candidate count, scan status, hashes remaining to process, candidates found, last scan time, and failures.
- Add filters/sorting for candidates: similarity, import date, file size, dimensions, repository, whether one side is already trash, and maybe tags.
- Support keyboard-driven review and batch operations once the semantics are safe.
- Provide a safe "delete this one" flow with confirmation and metadata consequences made visible.
- Add a way to regenerate perceptual hashes for selected files or all files after algorithm/threshold changes.
- Make false positives easy to dismiss and hard to accidentally recreate.

For long-term robustness:

- Add database constraints or canonical pair handling so `(A, B)` and `(B, A)` cannot exist as duplicate candidate/decision duplicates.
- Add scale tests or benchmarks for candidate generation across large libraries, and decide whether the in-memory index remains enough.
- Consider storing additional comparison metadata: dimensions, size, content type, import date, exact hash, perceptual hash algorithm/version, and perhaps a candidate score breakdown.
- Support richer media types with separate duplicate strategies: exact byte duplicates, image perceptual duplicates, video frame/clip similarity, and maybe archive/file-level duplicates.
- Build a proper duplicate relationship graph if Octans grows beyond one-off pair decisions.
- Add end-to-end tests through the UI/API path using real stored images, generated perceptual hashes, visible media URLs, and destructive keep-one decisions.
- Add observability around duplicate scans: files scanned, hash failures, candidate counts, elapsed time, memory use, and resolution counts.

## Thumbnails and Media Derivatives

Thumbnail creation, background thumbnail jobs, derivative storage, regeneration, cache invalidation, and display-oriented media metadata.

## Notes, Ratings, and User Metadata

Non-tag metadata attached to files: notes, rating systems, hash ratings, and any other user-authored annotations that should survive UI rewrites.

## Custom Scripting

User-defined commands outside website downloaders: command discovery, execution, inputs/outputs, sandboxing, error reporting, and integration points with files or tags.

## API Contracts and Frontend Agnosticism

The public contract a non-Blazor frontend would use: endpoint coverage, request/response types, error shapes, streaming/event mechanisms, versioning, and generated or documented clients.
