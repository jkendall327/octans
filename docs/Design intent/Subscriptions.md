# Subscriptions Design Intent

Rough design notes split out from the import design pass. This is not a full
implementation spec, but it records the conceptual boundary between
subscriptions, watchers, discovery jobs, and ordinary imports.

## Why This Matters

Subscriptions and watchers are how Octans repeatedly looks outward for new
media. They sit above importing: they discover candidate media sources and then
feed those candidates into normal import semantics.

The important split is that importing decides whether a concrete candidate
becomes media in Octans. Subscriptions and watchers decide when and how to look
for candidates.

## Discovery And Imports

- Discovery-backed workflows are layered into imports.
  - Gallery, post, watcher, and subscription-run workflows can be treated as
    import jobs from the user's perspective, but they have a discovery phase
    before concrete media-item import processing.
  - Discovery finds concrete candidate media sources that normal import rules can
    process.
  - The user can still experience "gallery import", "post import", or "watcher
    import" as holistic workflows.
  - The exact internal split between one phased job and parent/child jobs remains
    open.
- Discovery is intentionally simple.
  - It finds what the downloader/query produces.
  - Discovery does not deduplicate or validate candidate media sources.
  - Deduplication and validation belong to import processing and lower-level
    acquisition/HTTP layers.
- Narrowing discovery belongs to downloader/query configuration, not import
  filter semantics.
  - Import filters judge discovered candidates during import.
  - Discovery itself does not decide whether a candidate should be accepted into
    the library.

## Discovery Job Outcomes

- Discovery-backed jobs can complete with per-item rejections or failures.
  - If discovery succeeds and some discovered items are rejected by import
    filters, the overall workflow can still complete.
  - Per-item import results follow normal import semantics.
- Discovery failure is a job-level failure.
  - Examples include failing to fetch the gallery/post page or a downloader
    script crashing before concrete media candidates are discovered.
  - Partial discovery failure is also a job-level failure when the workflow's
    responsibility is complete enumeration.
  - For example, if a gallery job must enumerate all gallery pages and page 9 of
    10 fails, the gallery job failed even if items discovered from pages 1-8 were
    imported successfully.
  - Successfully imported items are not rolled back just because the
    discovery-backed job ultimately failed.
- Retrying a failed discovery-backed job conceptually reruns the whole
  discovery/import workflow.
  - Octans can rely on duplicate detection to skip media that was already
    imported by the previous attempt.
  - Discovery jobs do not need to checkpoint and resume from the exact failed
    discovery point by default.

## Watchers

- A watcher is a live-checking import workflow.
  - Conceptually, it is one job that rechecks its source every N minutes for new
    content.
  - It is one live job that accumulates discovered/imported item results over
    time.
  - It is not a series of separate subscription-like runs.
- A watcher completes when it repeatedly finds no new content over time and gives
  up checking for new material.
  - When a watcher reaches that completion condition, it should finish any
    current imports it already started.
- Watcher transient failure/retry behavior belongs to the lower-level downloader
  and HTTP machinery.
  - If those layers report an unrecoverable failure, the watcher job fails too,
    like any other import job.

## Subscriptions

- A subscription is different from a watcher.
  - A watcher is a live job.
  - A subscription is a long-lived scheduling/configuration concept.
- A subscription periodically spawns discovery/import jobs.
  - The subscription itself does not fail in the import-job sense.
  - Individual subscription runs/jobs can fail.
- Subscription runs should be the place where discovery, queueing, downloading,
  importing, and final reconciliation become observable.
  - This keeps the long-lived subscription as configuration/state, while each run
    records a concrete attempt.

## Decisions So Far

- Subscription runs should enqueue durable work rather than synchronously owning
  every downstream operation to completion.
  - Worker processing remains a separate responsibility.
  - Tests that need end-to-end imported media should explicitly drain the relevant
    workers instead of expecting subscription execution to do that implicitly.
- V1 already-seen tracking can key source history on subscription plus normalized
  discovered URL.
  - Downloader-specific stable source IDs, request fingerprints, or richer source
    identity can refine this later.
- Download/import provenance naming remains open.
  - In particular, it is not yet decided whether queued downstream work should
    describe its source type as subscription-owned, gallery-shaped, or through
    separate ownership and acquisition-kind fields.

## Subscription Configuration

- A subscription probably needs at least:
  - downloader/provider
  - query or seed URL
  - frequency
  - tags to apply to discovered imports
  - destination repository behavior
  - import filter settings
  - maybe per-source HTTP policy
- Subscription configuration should feed job settings into the jobs it spawns.
  - User-supplied subscription tags become job-level user tags for discovered
    media.
  - Destination repository and import filters apply to media discovered by that
    run.
- It remains open whether subscription configuration should reference downloader
  names directly or stable downloader IDs/versions.
  - Names are convenient.
  - Names are brittle if downloader files are renamed or upgraded.

## Already-Seen And Dedupe

- Subscription dedupe is a subscription concern, not an import concern.
  - Import can deduplicate by content hash once bytes are acquired.
  - Subscriptions may need to avoid reprocessing known source items before bytes
    are downloaded.
- A subscription likely needs an "already seen" or source-item history model.
  - It should key on stable source identity, request fingerprint, post URL, or a
    similar source-level concept rather than only final content hash.
  - This is what lets subscriptions skip known posts before downloading them
    again.
- How subscriptions use import/source/media facts for dedupe remains open and
  needs separate design.

## Scheduling And Lifecycle

- Subscription scheduling should eventually support:
  - enabled/disabled state
  - manual run
  - per-subscription in-progress tracking
  - protection against overlapping runs
  - failed-run recording
  - retry/backoff
  - continued processing when one subscription fails
- Subscription runs should expose enough state for the user to answer:
  - what is this subscription doing right now?
  - when did it last succeed?
  - when did it last fail?
  - what did it discover?
  - what import/download jobs did it create?

## Remaining Open Questions

- What exact data model should represent subscription source-item history?
- What exact state machine should subscription runs use?
- How should subscription-level retry/backoff interact with downloader/HTTP
  retry behavior?
- How should subscription scheduling be made fair across many subscriptions and
  providers?
- What provider/downloader identity should subscription configuration store?
