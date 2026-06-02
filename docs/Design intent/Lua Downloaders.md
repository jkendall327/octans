# Lua Downloaders Design Intent

This document records the intended role of user-supplied Lua downloaders in
Octans. It is not an implementation spec for every Lua function, but it defines
the product boundary: what Octans owns, what downloaders own, and what shape of
facts should cross between them.

## Why This Matters

Octans should not encode site-specific behavior for individual websites. A
downloader is the site-aware adapter that knows how one website represents
posts, galleries, searches, APIs, tags, source URLs, and other metadata.

Octans owns the application workflows around those facts: imports, HTTP policy,
subscriptions, job status, tagging destinations, source URL storage, bandwidth,
cancellation, and user-visible errors. The downloader tells Octans how to talk
to a site; it does not become the import system, HTTP subsystem, or subscription
scheduler.

Future cookie and credential handoff is important, but out of scope for this
pass.

## Core Boundary

- Raw media URL import is a simple download and bypasses Lua downloaders.
  - A user-entered raw media URL is not a downloader-backed post or gallery
    import.
  - If a post-style URL is submitted to the raw download flow, it can fail as an
    ordinary bad raw download.
- Downloader-backed workflows are explicit.
  - The UI/API caller chooses the workflow mode rather than Octans silently
    converting between raw download, post import, and gallery/search discovery.
  - Users generally choose sites and workflows, not specific downloader
    implementations.
- Octans performs all network I/O for downloader workflows.
  - Downloaders describe or derive requests and parse returned content.
  - Octans executes fetches so security, bandwidth policy, headers,
    cancellation, response limits, and observability stay centralized.
  - Lua downloaders should not directly fetch post pages, gallery pages, or API
    responses.
- Octans is not required to obey everything a downloader asks for.
  - A downloader-provided request description is a proposal, not a command.
  - Octans may filter, ignore, or override request details when policy,
    security, or implementation constraints require it.

## Site And Downloader Identity

- A downloader serves exactly one site, identified by one canonical site domain.
  - The canonical domain is the identity anchor for the site.
  - Alias, CDN, API, and media-host mechanics are separate from the source
    identity model.
- A downloader's implementation identity is the canonical domain plus the
  author-provided downloader name.
  - This allows multiple implementations for the same site, such as a scraper
    downloader and an API downloader.
- Source identity is site-scoped rather than downloader-scoped.
  - A stable source identity should survive changing which downloader
    implementation discovered or resolved it.
  - Conceptually, post identity is `site + post ID`, not `downloader + post ID`.
- Post candidates and post results may include a stable site post ID in
  addition to the canonical post URL.
  - The URL is useful and human-facing.
  - The site post ID can support canonicalization, URL-change resilience, and
    higher-level already-seen tracking.

## URL Routing And Normalization

- URL-to-downloader routing is based on declared metadata.
  - Given an arbitrary URL, Octans extracts the host and determines which active
    downloader can handle it.
  - Downloaders declare handled hostnames; Octans should not have to run an
    arbitrary `recognizes_url` script across downloaders.
- Routing metadata uses hostnames only for now.
  - Path-level routing is out of scope for the initial contract.
  - If one hostname contains multiple incompatible site areas, that can become a
    later design problem.
- Host routing does not define workflow support.
  - Handled hostnames determine which downloader owns a site URL.
  - Workflow support comes from declared capabilities and matching Lua
    functions.
- Downloaders normalize site URLs after routing.
  - A downloader may handle mobile URLs, old domains, short links, trailing
    junk, tracking parameters, and other site-specific URL messiness.
  - Post results should include the downloader-normalized canonical post URL,
    even if the input URL was already basically valid.
  - Octans does not need to preserve the original user-entered post URL once the
    downloader has produced canonical source identity.

## Post Resolution

Post URL resolution is the core required downloader capability.

- A post is a source object, not a synonym for one file.
  - A post may produce multiple media files.
  - Image sets, manga pages, videos plus related assets, alternate file
    versions, and similar cases should fit the model.
- Post handling is conceptually two-stage.
  - First, the downloader resolves and identifies the post as a source object.
  - Then it exposes media candidates and metadata from that post.
  - A downloader may implement this internally as one function, but Octans
    should not treat "post URL to raw media URL list" as the only meaningful
    contract.
- There is no separate post metadata refresh operation for now.
  - Resolving a post returns the current post metadata plus media candidates.
  - Octans decides whether a given workflow applies new metadata, downloads new
    bytes, or skips work.

## Gallery, Search, And Subscriptions

- Gallery/search discovery produces post candidates, not raw media URLs.
  - A gallery is a discovery mechanism for post imports.
  - Post resolution owns extracting media URLs and metadata from each post.
- Gallery/search discovery is optional.
  - A downloader can be used for gallery/search only if it declares the relevant
    capability and provides the matching Lua function.
- Discovery should be bounded by Octans.
  - Downloaders should expose paged or cursor-based discovery rather than
    "enumerate until exhausted."
  - Octans decides how much work to do for a manual workflow or subscription
    invocation.
  - This matters for bandwidth policy, scheduling, observability, cancellation,
    and per-run subscription limits.
- Discovery pagination should use an opaque downloader-controlled cursor for
  now.
  - Octans should not need to understand whether a site uses pages, offsets,
    timestamps, search-after tokens, or another paging model.
- Subscription/search discovery is based on a query plus site/domain, not a raw
  gallery URL as the core configuration.
  - When creating a subscription, the user specifies a query and the site whose
    active downloader should interpret it.
  - Subscriptions bind to the canonical domain, not to a specific downloader
    implementation.
  - Swapping the active downloader for a domain can affect future subscription
    runs, but it does not require subscription migration.
  - If a changed active downloader can no longer understand a subscription
    query, that is an ordinary validation or runtime subscription failure.

## Query Shape

- Octans parses and simplifies user queries before they reach Lua.
  - Downloaders should not each reimplement Octans query parsing.
  - Downloaders receive a structured list of Octans-level tags, terms, and
    options.
- The structured query contains only Octans-level query terms.
  - Octans does not need a universal schema for every site's search-specific
    knobs.
  - The user is responsible for providing a query that makes sense for the
    selected site.
- Downloaders should offer advisory prevalidation for structured queries.
  - Octans can ask whether the selected downloader can represent the user's
    query before starting discovery or saving a subscription.
  - Prevalidation is a UI/API helper, not a guarantee.
  - Runtime discovery can still fail or reject the query.

## Request And Response Shape

- Initial downloader request descriptions support GET requests only.
  - This covers ordinary page fetches and API query URLs.
  - POST and other methods can be reconsidered later if a real workflow needs
    them.
- Request descriptions may include custom headers.
  - Header names requested by the downloader should be declared up front in the
    JSON manifest.
  - Header values may be produced at runtime from the URL, query, cursor, or
    other request context.
  - User approval for requested header names is not required for now;
    inspection/logging is enough.
  - User-supplied cookies and credentials remain out of scope for this pass.
- Downloader parsers may accept raw response strings, parsed structured
  response values, or both.
  - For API-backed downloaders, Octans can parse common formats such as JSON
    before passing content to Lua.
  - If both raw and parsed forms are supported and available, Octans should
    prefer passing parsed structured data.
  - Raw strings remain necessary for HTML scraping and formats Octans does not
    parse.
- Accepted response forms are declared in metadata.
  - A downloader still implements one parser function per operation.
  - The same Lua function may accept raw strings, structured tables, or both.

## Downloader Results And Metadata

- Downloader output should use typed Octans concepts where practical.
  - Tags, source URLs, title, rating, artist, and similar concepts should be
    recognizable fields rather than an arbitrary site-specific metadata bag.
  - Site-specific escape hatches may be useful later, but the main contract
    should produce facts Octans knows how to route into imports, tagging, source
    URLs, notes, and media metadata.
- Results should support both post-level and per-media metadata.
  - Post-level metadata represents facts shared by the source post, such as
    artist, title, general tags, or site-specific fields.
  - Per-media metadata represents facts specific to one produced media file,
    such as page index, caption, variant/source URL, or file-specific tags.
  - Post-level metadata is expected to be stamped onto every media item produced
    by that post.
  - Exact import/tagging write semantics, conflict handling, and destination tag
    services belong to import/tagging design.
- Downloader-returned tags should be structured.
  - Tags should include namespace/category information rather than only raw tag
    strings.
  - Octans does not need to preserve original site tag text as a recovery
    mechanism for over-normalization.
  - Downloaders should avoid destructive tag normalization; if they do it
    anyway, that is downloader quality rather than Octans state responsibility.
  - Downloader-returned tags become ordinary external tags once imported.
  - Octans does not record the downloader/site as provenance on each tag fact.
- Source URLs should include both post URLs and raw media URLs.
  - The post URL preserves source context.
  - The raw media URL preserves the concrete acquisition location.
  - If a post produces multiple media files, the post URL should be attached to
    every produced media item.
- Site notes are future downloader metadata, but out of scope for the initial
  Lua downloader contract.
  - Eventually, downloader-returned site notes should become notes in the Octans
    sense.

## Failure Semantics

- Downloader workflow failure semantics are primarily downloader-authored.
  - The downloader decides when a post/gallery/search result is unacceptable
    enough to throw rather than returning a partial or empty result.
  - If a downloader throws, Octans can surface the downloader-provided message to
    the user.
  - Octans still owns ordinary downstream media download/import failures after
    the downloader has returned concrete media candidates.
- Downloader errors should have a small typed shape.
  - Initial fields can be message, code, and details.
  - If Octans receives typed downloader error fields, it should stamp them onto
    the relevant job status/history.

## Authoring Shape

- Downloader authoring remains folder-based with multiple Lua files.
  - The current general shape of one downloader folder containing multiple files
    by capability is preferred.
  - This keeps capability ownership visible and avoids one large script file.
- Downloader metadata should live in a non-executable JSON manifest.
  - JSON is less pleasant than TOML but more universal and straightforward to
    inspect as static contract data.
  - The manifest declares identity, handled hostnames, requested header names,
    capabilities, and accepted response forms.
  - Static validation checks that declared capabilities have matching Lua
    files/functions.

## Registration, Activation, And Rescanning

- Downloader definitions are read from disk, not copied into the database.
  - Octans rescans downloader files on app startup and when downloader
    management actions request it.
  - A user-facing "rescan downloaders" action should exist so users can hack on
    downloader files and ask Octans to reload them.
- A downloader is registered only if it has enough valid metadata to identify
  it.
  - Valid minimum metadata is the bar for Octans considering something a
    downloader.
  - Empty folders or totally malformed folders are scan/load errors rather than
    registered inactive downloaders.
  - Only registered downloaders appear on the downloader status/management page.
- Registered downloaders can be active or inactive.
  - Registered inactive downloaders still display their metadata, capabilities,
    and inactive reasons.
  - Inactive reasons include domain/hostname conflict, incoherent
    metadata/capabilities, and manual user choice.
  - Domain conflict, metadata/capability incoherence, and manual user
    deactivation are activation locks; all must be cleared before a downloader
    is active.
- Activation performs static contract validation only.
  - Octans validates required metadata, declared capabilities, matching
    functions, and domain/hostname conflicts.
  - Activation does not require Lua dry-runs or network self-tests.
  - Octans is not responsible for proving that a downloader works against a live
    site before activation.
  - If a downloader declares optional workflow support but the matching Lua
    function is missing or inconsistent, activation rejects the downloader
    rather than silently disabling that capability.
- Only one downloader should be active for a canonical domain/hostname set at a
  time.
  - Runtime URL routing should not depend on priority/order between conflicting
    active downloaders.
  - If activation conflicts with another downloader, Octans should offer:
    keep the existing downloader active and the new one inactive; activate the
    new downloader and deactivate the existing one; or deactivate both for now.
- Manual deactivation lives in an Octans-owned file in the downloaders
  directory.
  - The file can be a simple JSON dictionary or equivalent recording manually
    deactivated composite downloader identities.
  - Downloaders absent from the file are assumed active by default.
  - Domain-conflict and metadata/capability locks are computed during
    rescan/preflight rather than persisted as separate inactive-state reasons.
  - Manual user deactivation persists until the user lifts it manually.
- Rescanning is a full reconciliation pass.
  - It reruns static validation and domain/hostname conflict checks against the
    current files.
  - If a previously incoherent downloader now passes validation and has no
    manual deactivation or conflict lock, it becomes active.
  - If a rescan finds conflicting default-active downloaders, Octans should
    immediately record all conflicting downloaders as manually inactive.
  - Conflict-induced deactivation is slightly awkward but conservative: the user
    must deliberately reactivate a downloader after resolving the conflict.

## Out Of Scope And Open Questions

- Discovery cursor durability is unresolved.
  - It is not yet decided whether opaque cursors are durable subscription state
    or short-lived values within a single discovery run.
  - Durable cursors could help subscriptions continue efficiently across runs or
    restarts, but would make cursor compatibility part of the downloader
    contract.
- Downloader-provided expected file metadata is unresolved.
  - Media results might eventually include expected content type, size, hash,
    filename, or dimensions.
  - This may feed the existing HTTP download expectation/validation subsystem,
    but the boundary is not settled.
- Subscription already-seen granularity belongs to subscription design.
  - Whether subscription history is post-level only or also tracks produced
    media items should be decided in the subscriptions design doc.
