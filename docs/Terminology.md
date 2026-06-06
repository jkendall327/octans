# Terminology of note

*File*: bytes on a disk, the operating-system/filesystem notion of a file.
Local import candidates can be files, and Octans' stored bytes may live in
files, but once something enters Octans it is thought of as media, not as a
file.

*Image*: media whose content type is image-like, such as JPEG or PNG. Image is
a subtype of media, alongside other possible media types such as video or music;
it is not a synonym for media.

*Media*: the conceptual Octans object for a piece of importable content. Media
is the thing users tag, query, move between repositories, archive, trash, view
details for, and reason about. A media item is represented durably by a hash
item and may or may not currently have stored bytes.

*Media candidate*: a not-yet-imported candidate for media, produced by local
input, raw URL input, or a site adapter. The import pipeline may accept, reject,
skip, fail, or cancel a media candidate. Once it crosses the media-record
threshold, it is media rather than a candidate.

*Stored bytes*: the byte content associated with a media item when Octans
currently has it in storage. Stored bytes can be missing or reclaimed without
necessarily deleting the media item.

*Hash*: the content hash value computed from bytes. Hashes are used for media
identity, duplicate detection, and storage lookup, but a hash is only the value,
not the media item or its database record.

*Media record*: the durable database record that identifies one media item.
Today this is implemented as `HashItem`, keyed by content hash, but the domain
concept is the media record rather than the hash itself. A media item exists in
Octans when it has a media record.

*Media exists in Octans* when it has a media record. Stored bytes may be present,
missing, or reclaimed; that is a storage health/state concern, not the existence
criterion for the media item.

*Importing*: processing an external candidate until Octans either creates or
recognizes media, or records a terminal decision such as failure, rejection,
skip, or cancellation. Copying bytes into Octans-owned storage is only one
possible step within an import.

*Imported media*: media that has crossed the content-hash-plus-database-row
threshold. Later finalization, such as adding tags or source URLs, generating
thumbnails, or scanning for duplicates, can still be pending, missing, failed,
or recorded with warnings without changing whether the media has been imported.

*Acquisition mode*: the way an import job obtains or receives media candidates,
such as local files, raw URLs, posts, galleries, subscriptions, or watchers.
Acquisition mode is distinct from transport; for example, several acquisition
modes may eventually use network downloads.

*Raw URL*: an acquisition mode where the user supplies a direct candidate URL
for byte acquisition, without site-adapter identification or discovery. If the
URL is actually a post page or other non-media URL, raw URL import may fail as a
bad byte download; Octans does not silently convert it into a site-adapter
workflow.

*Import job*: durable tracked work that processes one acquisition mode's media
candidates under one set of job-level settings, such as destination repository,
user-supplied tags, and import filters.

*Job*: durable, user-visible tracked work. Prefer subsystem-specific terms such
as import job, download job, subscription run, or watcher job rather than bare
job when the owning subsystem matters.

*Download*: a transport/acquisition operation that retrieves bytes over HTTP or
another network protocol. Downloading can be one step within an import, but it
does not by itself mean the media has been accepted into Octans.

*Download request*: a submitted unit of desired network byte acquisition.

*Download job*: the durable tracked work created from a download request.

*Download manager*: the Octans subsystem that accepts download requests, runs
download jobs, tracks state, applies transport policy such as retries,
pause/resume/cancel, bandwidth limits, and exposes completion results.

*Source URL*: a URL recorded as media metadata that tells the user where media
was found, presented, or acquired. Source URLs can include post/page URLs and
direct media URLs. They are helpful evidence and can guide optimizations such as
skipping likely duplicate acquisition work, but they are not authoritative media
identity; the media record is the ultimate identity source.

*Source object*: a site-level thing identified by a site adapter, such as a post
or gallery item. A source object is not media in Octans, but it may yield one or
more media candidates.

*Post*: a kind of source object. A post is a site-level object that can expose
metadata and one or more media candidates. A post is not media, not a file, and
not necessarily one image. When a URL is treated as a post input, it identifies
zero or one posts: zero if the URL is broken, invalid, unsupported, or not a
post URL; one if the site adapter can identify the post. Multiple URLs may
identify the same post, so site adapters should canonicalize post identity.

*Identify*: determine the source object represented by a site input, such as a
URL, site ID, or query result. Identification produces source-object identity
and type, not imported media.

*Discovery*: a site-adapter operation that produces media candidates from a site
or source object. Discovery does not import media and does not create media
records by itself.

*Repository*: a mutually-exclusive lifecycle container for media.
Every piece of media is in exactly one repository at any point in time, and can
never exist outside a repository. If you sum the media in all repositories, you
get exactly the total media count for the Octans installation.

Repositories include:
- Inbox. The default intake/review repository for newly imported media: media
  that has not yet been explicitly admitted to Archive or disposed of in another
  way.
- Trash. Media the user has rejected from active interest. Octans may preserve
  the database row and metadata, but it is not obliged to preserve the original
  bytes. Trash is not a recoverable holding area by default.
- Archive. Media the user has explicitly admitted into their long-term library.
  Media only enters Archive through explicit user action.

Users can create arbitrary repositories. These are still lifecycle containers,
not overlapping collections or albums. For example, a user might create a
`staging` repository for media that should follow different review/admission
rules before being moved into Archive.

*Archive*, as a verb: move media from any repository into the Archive
repository. It does not mean importing, storing, or preserving media in the
generic sense.

*Site adapter*: user-created, site-specific logic that adapts a website's pages,
APIs, searches, posts, tags, source URLs, and media candidates into Octans-shaped
facts. A site adapter does not download bytes itself; it produces source objects,
metadata, and candidate URLs for Octans' download manager and import pipeline to
process.

*Subscription*: a long-lived scheduled configuration that periodically looks for
new media candidates from a site or source scope, then creates import work from
those candidates. A subscription is not bound to a specific site adapter
implementation; adapter selection is determined by site/hostname routing when a
run executes. The subscription itself is not an import job, though individual
subscription runs can create import jobs and can succeed or fail.

*Subscription run*: one concrete execution attempt of a subscription. A
subscription run has its own discovery work, queued/imported media candidates,
outcomes, and success or failure state.

*Watcher*: a single long-running live import workflow that repeatedly checks one
source for new media candidates until it completes. Unlike a subscription, a
watcher is not a long-lived scheduled configuration that spawns separate runs.
