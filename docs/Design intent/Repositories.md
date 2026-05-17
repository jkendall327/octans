# Repositories Design Intent

Rough design notes split out from the import design pass. This is not a full
implementation spec, but it records the conceptual role of repositories in
Octans: where media lives, how import placement interacts with lifecycle, and
what Trash means.

## Why This Matters

Repositories are the lifecycle universes media lives in. Importing, querying,
deletion, duplicate handling, storage repair, and source URL search all depend on
repository scope.

The key import-facing rule is simple: imports can place new or restored media
into a destination repository, but duplicate imports do not move existing
non-trash media.

## Core Repository Semantics

- A repository is a demarcationary universe that a piece of media lives within.
- Built-in repositories include:
  - Inbox: newly imported media by default.
  - Archive: media the user has elected to keep permanently.
  - Trash: media the user has deleted or does not currently care about.
- Users can create arbitrary repositories.
- Media belongs to a repository as part of its current lifecycle state.

## Import Placement

- An import job has one intended destination repository for successful new or
  restored media.
  - The default destination is Inbox.
  - `AutoArchive` is really a convenience policy for choosing Archive as the
    destination.
  - There is no conceptual reason imports could not target other user-created
    repositories later.
- Import destination applies to:
  - brand-new media
  - trashed/deleted media restored through `AllowReimportDeleted`
- Duplicate imports do not change a media item's repository.
  - If media already exists in Inbox, Archive, or another non-trash repository,
    importing the same bytes again may enrich tags/source URLs, but it does not
    move the media.
  - Media moves between repositories only through explicit user action, such as
    archiving or trashing.

## Trash Semantics

- Trash means the user does not currently care about the media.
  - Octans should not keep spending disk space or accumulating enrichment
    metadata for still-trashed media.
- Trashed media can exist as a database row without original bytes on disk.
  - This is an intended state.
  - Trash is not necessarily a recoverable trash folder.
- Source URLs and other accepted metadata are retained for trashed media while
  the media row exists.
  - Source URL search can include trashed media when the query's repository scope
    includes trash.
- If Octans re-encounters trashed media and `AllowReimportDeleted` is false:
  - `lastSeenUtc` may update because Octans recognized the bytes.
  - Octans should not add tags, source URLs, or other enrichment metadata.
  - Octans should not restore missing original bytes.
- If Octans re-encounters trashed media and `AllowReimportDeleted` is true:
  - the media is automatically untrashed
  - it moves to the import job's destination repository
  - if filters pass, it can accept the current import's tags and source URLs like
    any other successful import/enrichment

## Repository Movement

- Repository movement is explicit user lifecycle action.
  - Archiving moves media from Inbox to Archive.
  - Trashing moves media from Inbox, Archive, or another repository to Trash.
  - Future custom repository moves should be similarly explicit.
- Duplicate imports are not repository movement.
- Importing restored deleted media is the special import-driven movement case.
  - The user opted into this by allowing reimport of deleted media.
  - The destination repository comes from the import job settings.

## Query And Display Scope

- Repository predicates are normal query filters.
- Normal search should exclude Trash unless the query/repository scope includes
  it.
- `system:everything` should mean everything in the normal non-trash search
  scope, not literally every stored row including Trash.
- Source URL search, tag search, duplicate review, and other media views should
  respect repository scope.

## Storage And Repair Implications

- Non-trash media missing original bytes is an unhealthy state.
  - Re-encountering the same bytes can repair the missing original when
    practical.
- Trashed media missing original bytes is expected and should not be repaired
  unless the media is being restored.
- Deleting or trashing media can be destructive on disk while preserving the
  database row.
  - The UI/API should be honest about this: Trash does not necessarily mean
    recoverable bytes.

## Remaining Open Questions

- What exact semantics should user-created repositories have beyond Inbox,
  Archive, and Trash?
- Should repositories be mutually exclusive forever, or will some future concept
  need multi-repository membership?
- What exact UI/API should repository moves use?
- How should repository state interact with duplicate decisions and metadata
  merge workflows?
- What maintenance tools should report missing originals, orphaned files, and
  trashed rows with or without bytes?
