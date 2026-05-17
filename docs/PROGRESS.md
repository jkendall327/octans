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

## Data Model, EF, and Migrations

The database schema, EF Core models, migrations, persistence conventions, and whether durable state is modeled in the right place.

## Content Storage and Filesystem Layout

Hash-based storage, original media placement, repository folders, filesystem safety, path handling, cleanup, and deletion from disk.

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
- There is a real integration harness for the download manager using actual DI, queue/state/background/downloader services with the bottom HTTP edge faked.
- Tests cover queue ordering/restoration, state transitions, pause/cancel/retry, terminal notifications, staging cleanup, HTTP failures, transient retry, host circuits, per-domain concurrency, bandwidth pacing, disk-space checks, content-type validation, size limits, hash validation, request headers, and credential failures.

Known GitHub backlog:

- #180: deduplicate in-flight requests for the same URL.
- #183: make per-host connection pooling and concurrency limits explicit below the worker-level cap.
- #185: support conditional re-fetching with ETag/Last-Modified and `304 Not Modified`.
- #187: add structured metrics, not just structured logs.
- #188: add layered timeouts, especially response-header and idle/stall timeouts.
- #193: tracker for the hardening work; most child issues are closed, with the above still open.
- #202: route `RawUrl` imports through the download-job subsystem instead of `SimpleImporter.GetByteArrayAsync`.
- #203: move downloader discovery fetches behind a shared HTTP document/body-fetch abstraction.

Important current limitations:

- The shared subsystem is strong internally, but only partially integrated into product workflows. `PostImporter` queues downloads, but `RawUrl` imports still bypass it and download full remote files into memory.
- `DownloaderService` uses the named `DownloadClient` and shared header provider, but it still performs direct external HTTP document fetches instead of using a dedicated `Octans.Core/Http` abstraction.
- The durable queue does not deduplicate identical in-flight requests yet. `RequestFingerprint` exists, but there is no coalescing model or multi-subscriber completion model.
- Per-domain concurrency is enforced by the background worker, not by configured lower-level connection pooling or redirect-aware host accounting.
- Conditional requests are not implemented. Response ETag/Last-Modified are persisted, but there is no first-class re-fetch request path or `NotModified` handling.
- Timeout policy is still coarse. `HttpDownloader` sets a long overall `HttpClient.Timeout`; there are not distinct connection, header, overall, and idle/stall timeouts.
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

## Thumbnails and Media Derivatives

Thumbnail creation, background thumbnail jobs, derivative storage, regeneration, cache invalidation, and display-oriented media metadata.

## Notes, Ratings, and User Metadata

Non-tag metadata attached to files: notes, rating systems, hash ratings, and any other user-authored annotations that should survive UI rewrites.

## Statistics and Storage Reporting

Home stats, storage usage, repository counts, media counts, and any aggregate reporting needed for status pages or operational visibility.

## Progress and Notifications

Cross-subsystem progress reporting, background progress stores, status updates, UI notifications, and any future event stream such as SignalR, SSE, or WebSockets.

## Background Work and Scheduling

Hosted services, channels, outbox-like flows, startup recovery, concurrency rules, job ownership, shutdown behavior, and how background work coordinates through the database.

## Configuration and Settings

Application settings, user settings, feature options, keybindings, per-subsystem options, environment-specific configuration, and what belongs in durable settings versus local config.

## Custom Scripting

User-defined commands outside website downloaders: command discovery, execution, inputs/outputs, sandboxing, error reporting, and integration points with files or tags.

## API Contracts and Frontend Agnosticism

The public contract a non-Blazor frontend would use: endpoint coverage, request/response types, error shapes, streaming/event mechanisms, versioning, and generated or documented clients.

## Observability

Structured logging, metrics, traces, health checks, operational status, failure visibility, and enough diagnostics to understand what long-running background work is doing.

## Performance and Scalability

Large-library behavior: database indexes, query planning, filesystem fan-out, thumbnail throughput, import/download throughput, duplicate scan cost, memory use, and UI/API pagination pressure.

## Reliability and Recovery

Crash recovery, idempotency, partial work cleanup, durable job state, startup reconciliation, retry policy, data integrity, and how safely the app behaves when interrupted.

## Security and Sandboxing

Downloader script sandboxing, custom command risk, credential storage, HTTP header handling, local file access boundaries, path traversal protection, and future multi-user implications if any.

## Testing and Verification

Unit tests, integration tests, real database coverage, filesystem fakes, HTTP fakes, migration tests, API tests, and the commands that should prove each subsystem still works.

## Documentation and Operability

User-facing docs, developer docs, architecture notes, subsystem READMEs, troubleshooting guides, local setup, migration guidance, and operational runbooks.

## Current UI Shell

The Blazor UI, view models, and component wiring. This matters as proof that workflows are usable today, but it is intentionally secondary to the backend and API surface because the frontend may change.
