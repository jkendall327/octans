# Imports Design Intent

Rough design notes from a Q&A pass. This is not a full implementation spec, but
it records the intended behavior for Octans imports: what it means to bring
potential media under Octans' purview, where importing stops, and how import
semantics connect to tags, source URLs, repositories, downloading, and
discovery.

## Why This Matters

Importing is the main intake path for Octans. Local files, raw URLs,
discovery-backed media, tag assignment, source provenance, repository placement,
and future API/UI boundaries all depend on the same semantics.

The product stance is Hydrus-like and archival for accepted media facts:
Octans should preserve useful facts about media it accepts. Import job history
should explain what happened during an import, but it does not need to become a
forever-queryable audit warehouse.

## Core Semantics

- An import is an attempt.
  - An import can succeed, fail, be skipped, or be cancelled.
  - A failed or cancelled import is still a real import attempt.
  - Import item history should record why a piece of potential media succeeded,
    failed, was skipped, or was cancelled.
- Media exists in Octans once both of these have happened:
  - a content hash has been created
  - a database row has been inserted for that hash
- This hash-plus-row moment is also when an item counts as imported.
  - Tags, thumbnails, source deletion, duplicate scanning, and other follow-up
    work are not central to the essence of being imported.
- If cancellation happens before this moment, no media exists.
  - Octans can freely discard temporary bytes or staging artifacts for that item.
  - The cancelled item remains only as import job history.
- If cancellation arrives after the media row exists, Octans has crossed the
  point of no return for that item.
  - Octans should continue finalization for that item so the media is left
    coherent.
  - Later items in the same job can still be cancelled.
- Required finalization after the point of no return includes:
  - adding user-supplied tags
  - adding accepted external/imported tags
  - adding accepted source URLs
  - updating `lastSeenUtc`
- If required finalization fails after the media row exists, the item can still
  count as successfully imported with a warning.
  - Octans does not need an automatic repair queue for metadata finalization
    warnings.
  - It is acceptable to lose that specific tag/source addition as long as the
    user is told what happened.
- Desired but non-essential follow-up includes:
  - enqueueing thumbnail work
  - enqueueing perceptual-hash/duplicate work
  - deleting the local source when requested
- Media can exist without original bytes on disk.
  - This is the natural state for trashed items.
  - If a row exists with a content hash but original-byte storage failed, the
    current leaning is to count that as imported but unhealthy.
  - This unhealthy-row case remains open and needs firmer storage/reconciliation
    design.
- Media can exist without tags.
- Media can exist without thumbnails.
- Importing is concerned with turning an external source candidate into a
  durable media item or a durable terminal decision.

## Import History

- Import attempts are processing/audit records, not the long-term identity of a
  media item.
  - Media details do not need to present every import event as part of the
    media's core identity.
  - Later duplicate imports may enrich a media item with tags or source URLs, but
    they do not redefine when that media entered Octans.
  - Media facts do not need to retain a durable link to the mechanism that added
    them.
  - If a workflow wants to leave its mark on media, it can apply a tag during
    import.
- An import attempt record does not need to relate to a media item.
  - Pre-download rejection, unsupported input, cancellation, and other terminal
    outcomes may exist without a content hash or media row.
  - These records are mainly useful in the context of their import job.
  - Octans does not need a first-class long-term search surface for every failed
    import attempt.
- Import job history can be pruned over time.
  - The cleanup period should be configurable.
  - Users may be able to disable cleanup if they want to keep everything.
  - Pruning job history must not remove accepted media facts such as source URLs,
    tags, or media timestamps.

## Media Import Timestamps

- Media should record the first time it was imported into Octans.
  - This should be a durable media-level concept such as `firstImportedUtc`.
  - It is set when the database row is created with the content hash.
  - It is used for sorting, statistics, and similar library-level behavior.
  - It survives trashing/deletion and later reimport.
  - Reimporting previously deleted media does not reset first import time.
- Media should also record the last time Octans encountered it.
  - This should be a media-level concept such as `lastSeenUtc`.
  - Put precisely, `lastSeenUtc` means Octans identified that the hash of a
    given byte sequence matched the hash of a pre-existing media item.
  - On brand-new imports, `lastSeenUtc` starts equal to `firstImportedUtc`.
  - It updates when Octans identifies a hash match, even if the encounter adds no
    tags, source URLs, or other metadata.
  - It updates if cancellation arrives after Octans has identified the hash
    match.
  - It updates when Octans recognizes previously deleted/trashed media and the
    current import policy refuses to reimport it.
  - It still updates when current filters prevent metadata enrichment.
  - `lastSeenUtc` is separate from first import time and should not disturb
    import-time sorting.

## Boundaries

- Downloading is one method of acquiring bytes for an import.
  - HTTP download policy, byte transfer, transfer pause/cancel, bandwidth
    limiting, and retry behavior belong to the download subsystem.
  - Users do not control byte-level transfers through the import job UI.
  - Download pause/cancel is a separate axis owned by the download subsystem.
  - Import jobs can still be incidentally affected by download controls, such as
    a global "stop new network traffic" command.
- Thumbnail generation and duplicate searching are triggered by imports but are
  not part of importing.
  - An import can complete before thumbnails or perceptual hashes exist.
  - Separate background jobs should find and repair media without thumbnails or
    perceptual hashes.
- Discovery is intentionally simple.
  - It finds what the downloader/query produces.
  - Narrowing discovery belongs to downloader/query configuration, not import
    filter semantics.
  - Discovery does not deduplicate or validate candidate media sources.
  - Deduplication and validation belong to import processing and lower-level
    acquisition/HTTP layers.
  - If the same candidate appears multiple times and is processed multiple times,
    later attempts use normal duplicate semantics such as `AlreadyInLibrary`.
- Gallery, post, watcher, and subscription-run semantics are owned by their
  discovery/workflow designs.
  - From the import side, they eventually provide candidate media sources plus
    job settings, and normal import rules process those candidates.

## Source URLs

- Source URLs are plain media-level facts.
  - They are associated with media items, not with import jobs as such.
  - They do not need their own service/repository context.
  - There is no deep ontological difference between a post/page URL and a direct
    media URL.
  - If the same media appears on Gelbooru and Danbooru, both source URLs can be
    attached directly to the media.
- Octans does not care about local filesystem origin paths as durable source URL
  provenance.
  - Local paths are acquisition details.
  - Local imports can optionally derive tags from paths, but that is tag
    behavior, not source URL metadata.
- Octans does not need to store subscription identity or downloader identity as
  core media provenance.
  - Those processes can apply tags during import if they want durable source
    information.
- Users can manually add and remove source URLs as ordinary media metadata.
  - Source URLs are simple current facts.
  - If a source URL is removed from a media item, Octans does not need to
    preserve source-URL deletion history, apart from whatever old import job
    history may already say.
  - Manually added source URLs are user-authored references, not claims Octans
    must validate against the media bytes.
  - Octans does not need to check whether a manually added URL still resolves to
    the same content hash.
- Automatic imports should add source URLs only after media has been successfully
  recognized and attached to a media item.
  - Failed import attempts keep their URL facts in import history, not in media
    source metadata, because there is no media item to attach them to.
  - For raw URL imports that redirect, Octans should remember the final byte URL,
    not necessarily the originally requested URL.
- Source URLs are retained for trashed/deleted media while the media row exists.
  - Repository/query semantics decide when trashed media participates in source
    URL search.
- Source URLs do not define media identity or duplicate identity.
  - Bytes are authoritative for duplicate identity.
  - URLs are useful metadata for users.
  - Linkrot and mutable remote URLs make URL-based identity too dangerous.
- Source URLs should be searchable.
  - Search should eventually support explicit source-domain queries.
  - Search should also offer a separate URL-regex or URL-pattern flow for cases
    where the user wants to match against source URL strings directly.
  - Domain search and URL-pattern search are distinct concepts, not one vague
    substring search.
- Source URL variants should be preserved for now rather than aggressively
  normalized or deduplicated.
  - URL normalization is tempting, but site-specific equivalence rules are a
    later design problem rather than a commitment in the import semantics.

## Tags On Import

- Imports may create new tags automatically.
  - Requiring human approval for every new imported tag would be too
    labour-intensive.
- Tag destination is based on authorship, not merely on which import workflow
  carried the tag.
  - Tags explicitly supplied by the user during an import go to "my tags".
  - Tags discovered from downloaders, external sources, or automated workflows go
    to the automatic/external tag service.
- User-supplied import tags are job-level tags applied to every item in the
  import job.
  - This lets the user express batch intent, such as "everything in this import
    gets my `foo:bar` organisation tag."
  - For discovery-backed imports, user-supplied workflow tags apply to every
    media item discovered by that workflow.
  - Per-item user tag assignment is not part of the core import semantics for
    now.
- External/discovered tags are naturally item-level because each downloaded item
  or post can produce different tags.
  - External tags should be written faithfully onto the media.
  - Canonical display/search behavior can still make those tags behave like their
    canonical sibling tags later.
  - Automated imports should only add tags to the automatic/imported tag service,
    once such services exist.
  - They should not mutate the user's manual "my tags" service.
- Octans does not record tag removals as durable suppression facts.
  - If an automated workflow later rediscovers an external tag that the user
    removed, it may add that tag back to the external tag service.
  - It still must not touch "my tags".
  - Tag services protect the user's own tag space from noisy or hostile external
    taggers.
- Choosing which tag services are active for search/display is not an import
  concern.
  - Imports write tags into the appropriate service.
  - Querying and display decide which services are visible or active.
- Human-authored tag writes can suggest canonical tags when sibling
  relationships exist, but Octans should not force canonicalization.
  - If Octans cannot ask the user, preserving the raw noncanonical tag is the
    safer default.
  - Downloader/import-provided tags carry source provenance, so preserving the
    raw external tag is valuable.
- Tag service semantics are not pinned down here.
  - The current intuition is that automatically imported/downloader tags may go
    into their own tag service/repository, while manual human-authored tags live
    in a "my tags" service.
  - This may be the quarantine mechanism for ugly, noisy, or hostile external
    tags.

## Local File Semantics

- Octans owns its own storage folders.
  - It does not manage files in arbitrary external filesystem locations.
  - Managed external files are out of scope for now.
- Local filesystem paths are not durable provenance metadata, but local imports
  can derive tags from path parts when the user asks for it.
  - A local import option can record the original filename as a normal tag.
  - That filename tag can use an optional namespace, defaulting to `filename`.
  - A local import option can record folder names as normal tags.
  - Folder tags can use an optional namespace, defaulting to `folder`.
  - Folder-derived tagging should support capturing more than the immediate
    parent folder.
  - The user can choose a bounded path depth, such as "capture up to N folder
    entries above the imported file."
  - When capturing multiple folder entries, Octans should lean toward creating
    one normal tag per folder segment rather than one combined path string.
  - A separate option can capture the full path from the drive/root.
  - Full-path capture is also recorded as an ordinary tag when enabled.
  - Full-path tags default to the `path` namespace.
  - The user is responsible for choosing whether storing local path information
    is appropriate for their library.
  - This avoids hard-coding arbitrary Hydrus-style choices like parent folder and
    grandparent folder while keeping the useful behavior.
  - Filename, folder, and path-derived tags preserve the exact string, including
    case, spaces, punctuation, and file extension.
  - Users may have their own filesystem naming schemes, and Octans should not
    silently clean them up.
  - Path-derived tags follow the same raw-preservation bias as other imported
    tags. If a sibling/canonical form exists, Octans may suggest it to the user
    in an interactive flow, but non-interactive imports should preserve the raw
    derived tag.
  - User-selected local filename/folder/path tag derivation writes to the user's
    manual "my tags" service, not to the automatic/imported tag service.
  - If a duplicate local import has filename/folder/path tag options enabled,
    those derived tags merge onto the existing media item like any other import
    tags.
- Local imports should move or delete external bytes only after the media is
  confirmed safe inside Octans' domain.
  - At minimum, source deletion should wait until the import has stored the media
    row and the content-addressed original bytes.
- "Delete after import" means the user wants the external local source removed
  after safe ingestion.
  - It must not delete the source if the import failed, was rejected, or never
    made it safely into Octans storage.
  - It should delete the source for any successful import outcome, including
    `ImportedNew`, `AlreadyInLibrary`, `RepairedMissingOriginal`, and
    `RestoredDeleted`.
  - If source deletion fails after a successful import, the import remains
    successful with a warning.
  - Deleting source material is a best-effort convenience, not part of the core
    import guarantee.
  - Failed source deletion does not need an automatic retry workflow.
  - The warning is enough for the user to act manually if they care.

## Raw URL Semantics

- `RawUrl` means "get this URL and see what Octans can do with it."
  - It is not a guarantee that the URL points directly at valid media.
  - It may fail with a result such as "this is raw HTML and not anything Octans
    can import."
- Raw URL imports should produce clear terminal results for HTTP/download
  failures, unsupported content, filter rejection, and successful import.

## Filters And Policy

- Import filters are semantic policy, not just convenience pre-checks.
  - A user should be able to see which rule caused a source to fail import.
  - Filters can inspect proposed tags and source facts, not only bytes or media
    dimensions.
  - Tag blacklisting is conceptually an import filter.
  - URL/domain filters may also exist, but tag blacklisting is the clearer
    motivating case.
- Tag blacklist policy applies to external/automatic tags and broad automated
  intake, not to deliberate user-authored tags.
  - The blacklist has no authority over the user.
  - It exists to keep broad download/import sprees from swamping the library with
    unwanted material.
  - A discovery workflow may intentionally search for one tag while rejecting
    discovered media that also carries a blacklisted tag.
  - The user should not have to add the same negated tag to every downloader or
    discovery query to enforce a general import blacklist.
- If an import item carries user-supplied tags but external discovered tags trip
  a blacklist/filter, the item is rejected by default.
  - The escape hatch is an explicit per-import-job filter override.
  - Users can temporarily override blacklist or other filter policy when they
    deliberately want a specific import to bypass current standards.
- Filter overrides should support both granular per-filter control and an
  all-filters override for a given import.
  - Conceptually, the user can turn individual filters on/off for a job, or say
    "ignore all filters for this import."
- A given import job has one set of active filter criteria, applied to every item
  in that job.
- Duplicate/already-imported detection should happen before current import
  filters reject the content.
  - If media was accepted into Octans in the past, that acceptance continues even
    if today's filters would reject the same bytes.
  - Filters have no retroactive authority over already-imported media.
  - Present filters still have present authority.
  - A duplicate import that fails current filters should not enrich existing
    media with new tags, source URLs, or similar metadata.
  - This matters for future policy such as tag blacklists: a blacklisted tag
    should not be reintroduced merely because the bytes already exist.
- Filter failures should be durable item results.
  - Size, filetype/content-type, resolution, tag blacklist, and future policy
    failures should be visible in import history.
  - If a tag/source-aware filter rejects an item, the import result should record
    the relevant rejected fact enough to explain the decision.
  - Rejected tags or source facts may appear in job history as explanation even
    though they are not accepted as media metadata.
- Import jobs do not need to store the complete active filter configuration.
  - If an item would fail multiple filters, Octans can record the first filter
    tripwire that rejected it.
  - The important audit fact is the concrete rejection reason, not a pedantic
    snapshot of every policy setting.
- Filter evaluation order should be deterministic.
  - The final ordering is not settled, but it should respect when information is
    available.
  - Some filters can run before byte acquisition, such as URL filters or
    discovered-tag blacklists.
  - Some filters can run from transfer metadata, such as HTTP `Content-Length`.
  - Some filters require downloaded bytes or decoded media, such as aspect-ratio
    filters.

## Duplicate, Reimport, And Repair Semantics

- If imported content already exists in a non-trash repository, the item should
  be a successful skip.
  - The user-facing result should say that the item was already imported.
  - `lastSeenUtc` still updates because Octans recognized the bytes.
- A skipped duplicate import may still mutate media metadata.
  - "Already imported, added 1 tag" is a valid result.
  - "Skipped" means Octans did not create a new media item or store new original
    bytes, not that the encounter had no useful side effects.
  - If duplicate content is imported with accepted new tags, those tags should be
    merged onto the existing media item.
  - If duplicate content is imported with accepted new source URLs, those source
    URLs should be associated with the existing media item.
- Duplicate imports do not change a media item's repository.
  - Import destination applies to new media and restored deleted media, not to
    already-existing non-trash media.
  - Broader repository lifecycle rules are owned by repository semantics.
- If imported content was previously deleted or trashed, reimport behavior is
  controlled by `AllowReimportDeleted`.
  - This is a per-import option.
  - A global policy may provide the default value for new imports.
  - When `AllowReimportDeleted` is true, importing trashed/deleted media
    automatically untrashes it and moves it to the import job's destination
    repository.
  - If filters pass, restored deleted media accepts the current import's tags and
    source URLs like any other successful import/enrichment.
  - If `AllowReimportDeleted` is false, encountering deleted media updates
    `lastSeenUtc` but should not add tags, source URLs, or other enrichment
    metadata.
  - Rejected deleted-media encounters should not keep bloating metadata for that
    item.
- If Octans has a media row for a content hash but the original bytes are missing
  from storage, re-encountering the same bytes should repair the missing original
  when practical.
  - This can happen even though the item is otherwise already imported.
  - The import result should be explicit, for example
    `RepairedMissingOriginal`, rather than hiding this under a generic
    already-imported result.
  - This repair should not happen for trashed/deleted media when reimport is not
    allowed.
  - Restoring original bytes for still-trashed media would work against trash
    semantics.
- Reimport outcomes should be precise.
  - "Already in library", "previously deleted", "restored", "repaired missing
    original", and "rejected by policy" should not collapse into one vague
    result.

## Job And Control Semantics

- A given import job has one acquisition mode.
  - Local files, raw URLs, and discovery-backed workflows should create separate
    jobs rather than mixing heterogeneous source candidates into one job.
  - The acquisition modes are different enough in code and user mental model that
    forcing one mixed job shape is not worthwhile.
- A given import job has one intended destination repository for successful new
  or restored media.
  - The default destination is Inbox.
  - `AutoArchive` is really a convenience policy for choosing Archive as the
    destination.
  - There is no conceptual reason imports could not target other user-created
    repositories later.
- Import job settings apply to all items in the job.
  - Destination repository, user tags, and filter settings apply to every item.
  - For discovery-backed jobs, they apply to every discovered media item.
- Import job settings may be changed while a job is running.
  - Setting changes apply prospectively to new imports only.
  - Changing destination repository, user tags, or filter settings does not
    retroactively change already-processed items.
  - Job settings are atomic.
  - Replacing the job's user-tag settings replaces the tag set for future items;
    Octans does not remember or merge past setting snapshots.
  - Settings apply when an item is imported, not when it is discovered.
  - Already-discovered but not-yet-processed items use the current settings when
    they begin import processing.
  - Item history should record relevant item details and outcomes, not a full
    versioned history of job settings.
- A user controls imports at the item/job level.
  - Items within a job are processed sequentially for now, Hydrus-style.
  - Import pause/cancel should try to interrupt mid-item processing where
    practical through cooperative cancellation.
  - It is acceptable that some operations only observe cancellation at natural
    checkpoints, but the conceptual goal is not limited to between-item control.
- Import jobs complete even when some items are rejected or fail.
  - Per-item rejection/failure is normal and expected.
  - A job-level failure means the import process itself broke in a serious way,
    such as losing track of what items it was meant to process.
- Cancelling a job does not roll back already-imported items.
  - Items that succeeded before cancellation stay imported.
  - The job can be marked `Cancelled` as a terminal label for the remaining
    workflow.
- Multiple import jobs may run at once.
  - Each individual job processes its items sequentially.
  - Parallelizing items within a job is an interesting future option, but not the
    default semantic model.
- Resume after restart means resume pending items only.
  - Failed items are retried only through explicit user action.
  - Cancelled items are terminal unless the user creates or requests new work.

## Result Vocabulary

A useful import item result vocabulary should include at least:

- `ImportedNew`
- `AlreadyInLibrary`
- `RestoredDeleted`
- `RepairedMissingOriginal`
- `RejectedByFilter`
- `FailedToRead`
- `FailedToDownload`
- `UnsupportedMedia`
- `Cancelled`

The exact enum/table/API shape can change, but these outcomes should remain
distinct in the user's mental model.

## Current Implementation Notes

The current code already points in this direction, but only partially:

- Durable import jobs exist for file and raw URL imports.
- Import jobs record job/item state and process items sequentially.
- Content identity is content-hash based through `HashItem`.
- New media defaults to Inbox unless `AutoArchive` places it in Archive.
- Imports can attach tags per source.
- Reimport logic can reactivate previously deleted hashes.
- Raw URL import currently acquires bytes through the HTTP download subsystem,
  but import/download handoff semantics are still rough.
- Post/gallery/watchable import concepts still appear in some core models even
  though design intent treats them as discovery-backed workflows.

## Remaining Open Questions

- What exact table shape should source URL metadata use?
- What exact global policy should provide defaults for `AllowReimportDeleted`,
  destination repository, source URL recording, and filter behavior?
- What exact semantics should tag services/repositories have?
- What does explicit retry of failed import items look like in the API and UI?
- How should multiple concurrent import jobs be scheduled fairly against each
  other and against download/network policy?
- What exact API DTO should represent an import item terminal result?
- Should `lastSeenUtc` become a visible/sortable UI/query concept, or remain
  mostly internal/statistical?
- If a database row with a content hash exists but original-byte storage failed,
  should Octans always treat that as imported-but-unhealthy, or are there cases
  where the media row should be rolled back?
