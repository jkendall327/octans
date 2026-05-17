# Querying Design Intent

Rough design notes from a Q&A pass. This is not yet a full implementation spec,
but it records the intended behavior for Octans querying, tags, siblings,
parents, and the frontend/API contracts that depend on them.

## Why This Matters

Querying and search are central to Octans. Tags, parent/sibling relationships,
repository state, gallery behavior, API contracts, and future frontend choices
all depend on the same semantics.

The main product stance is: Octans should behave like a strict archival/tagging
tool, not a fuzzy relevance search engine.

## Core Query Semantics

- Plain multi-term queries use strict Hydrus-style filtering.
  - Every positive predicate must match.
  - Negative predicates subtract from the matched result set.
  - Querying is a filter language, not loose discovery search.
- Empty raw input is normalized to `system:everything`.
  - Empty semantic queries are not a real query shape.
  - Stand-alone negative queries are treated as hanging off
    `system:everything`, so `-character:bowser` means
    `system:everything -character:bowser`.
  - `system:everything` means everything in the normal non-trash search scope,
    not literally every stored file including trash.
- Negation is semantic set subtraction.
  - A negated predicate removes every result that would match the negated
    criteria.
  - The implementation does not have to literally execute by subtraction, but it
    must behave as if it does.
  - Negation applies to implied parent tags too: if media effectively has
    `series:mario`, `-series:mario` excludes it.
- Space-separated predicates are AND.
- OR must be explicit and unambiguous.
  - OR is supported only through explicit OR groups.
  - `(or:character:mario OR character:luigi) game:smash` means: files matching
    `game:smash` and one or more of `character:mario` or `character:luigi`.
  - Loose OR precedence should not be inferred from free text.
  - XOR/exclusive-or is out of scope.
- Wildcards match the effective semantic tag set.
  - Wildcard predicates should see canonical sibling behavior.
  - Wildcard predicates should see inherited parent tags once relationship state
    has been materialized.
- Normal search should be case-insensitive.
  - Stored tags preserve case because case can have semantic import.
  - A future explicit case-sensitive search flag may be useful, but it should not
    be the default.
- Raw-tag search is not a normal grammar priority.
  - Searching stored raw tags may become a debug or maintenance flag later.

## Tag Normalization

- Normalize as little as practical.
- Trim leading/trailing whitespace.
- Reject empty subtags.
- Allow empty namespaces.
- Do not lowercase namespace or subtag values for storage or semantics.
- Preserve underscores, spaces, punctuation, and similar spelling differences.
  Resolving those differences is the job of sibling relationships, not hidden
  normalization.
- The UI may choose to display tags as lowercase, but storage/query semantics
  should not silently discard case.

## Repository Predicates

- Repository predicates are ordinary filters.
- Default searches include non-trash repositories such as inbox and archive, and
  exclude trash/deleted files.
- `system:everything` follows that default non-trash scope.
- `system:trash` is required to include trashed files.
- `system:trash character:mario` uses normal AND logic: both predicates must be
  true.
- `system:trash` is not an OR mode or special override beyond changing the
  repository scope of the query.

## System Predicates

System predicates are less urgent to pin down than tag/query semantics. They are
conceptually easier to bolt on later, even if individual predicates still take
implementation work.

A plausible first useful batch:

- `system:everything`
- `system:inbox`
- `system:archive`
- `system:trash`
- `system:filetype`
- `system:imported_after`
- `system:imported_before`
- `system:tag_count`

Width, height, filesize, and richer media predicates can wait until the media
metadata model is firmer unless they become pressing.

## Query Grammar and Errors

- The real query grammar should be unambiguous, even if the UI is friendlier.
- The UI will likely provide structured controls for OR groups rather than
  expecting users to type complex grouped queries directly.
- The UI can translate query-builder state into the real grammar.
- Malformed or unsupported predicates should return clear structured errors.
  - Invalid syntax should explain why it is malformed.
  - Valid but unimplemented predicates should explain that they are unsupported.
  - Predicates must not be silently ignored.
  - Errors should include stable codes and messages.
  - Where possible, errors should include token/range information such as start
    offset and length so the UI can highlight the offending part of the query.
  - V1 range information can be rough, but the contract should be designed for
    robust parse/validation feedback.

## Parent Tags

- Parent tags are semantic inheritance.
  - If tag A is the parent of tag B, media with tag B effectively has tag A.
  - If `series:mario` is a parent of `character:mario`, media tagged
    `character:mario` should match `series:mario` once relationship state has
    been materialized.
- Parent relationships are transitive.
  - If `franchise:nintendo` is parent of `series:mario`, and `series:mario` is
    parent of `character:mario`, then `character:mario` implies
    `franchise:nintendo`.
- Parent cycles should be rejected.
- Parent tags are implied, not manually assigned file tags.
  - Parent inheritance should materialize into derived/cache-backed state, not
    normal raw file-tag mappings.
  - Tag reads should distinguish direct tags from implied parent tags, for
    example with an `isImplied` marker.
  - An implied parent tag should not be removable from a single file. To remove
    that implication, change the parent relationship.

## Sibling Tags

- Sibling tags canonicalize non-ideal tags into a chosen good tag.
  - Multiple spellings or forms of Spider-Man should collapse into one chosen
    form for normal display/search.
  - Creating a sibling relationship expresses that the user wants to work with
    the canonical tag in normal workflows.
- Octans should follow Hydrus's broad reversible path.
  - Raw file-tag mappings preserve the tag that was actually assigned or
    imported.
  - Sibling relationships create derived/display/search truth over those raw
    mappings.
  - Removing a sibling relationship means that canonicalization dictate is no
    longer in force; it does not necessarily annul all semantic consequences that
    were discovered while the sibling group existed.
  - This complexity is worth accepting because tags and querying are core to the
    app.
- Normal search, display, and autocomplete should use canonical/display
  semantics.
  - If `creator:bob` resolves to `creator:robert`, searching for
    `creator:bob` behaves like searching for `creator:robert` once derived state
    has been materialized.
  - Non-canonical sibling tags can still be accepted as input aliases.
  - Autocomplete should suggest the canonical tag when the user types an alias,
    with alias-match annotation where useful.
- Sibling relationships are transitive from the user's perspective.
  - If `A -> B` and `B -> C`, then both A and B resolve to C.
  - Direct and indirect sibling cycles should be rejected.
  - A sibling chain needs a canonical endpoint.
- Sibling resolution feeds parent inheritance.
  - If `character:spiderman` resolves to `character:spider-man`, and
    `series:marvel` is a parent of `character:spider-man`, then media whose raw
    tag is `character:spiderman` should effectively match `series:marvel` once
    derived state has been rebuilt.
  - Parent relationships authored against non-canonical tags also need to
    participate in the effective graph.
  - Sibling groups represent one concept expressed through multiple tags, so
    parent implications from any member of the group apply to the canonical tag.
  - Example: if `spiderman` implies `marvel`, `spodermon` implies `superhero`,
    and `spider-man` implies `comic-book`, then canonical `spider-man`
    effectively implies all three parents.
  - The parent relationship rows do not have to move in the physical universe;
    they become available when Octans computes whether the canonical concept
    implies another tag.
  - Parent implications flow through the current sibling equivalence graph only.
  - If a sibling relationship is removed, parent implications borrowed through
    that old equivalence set disappear unless they also exist through another
    current relationship.
  - Example: if `major kusanagi` implies `ghost in the shell` and is accidentally
    siblinged to `renamon`, `renamon` may effectively imply
    `ghost in the shell` while that sibling relationship is active. Removing the
    mistaken sibling relationship should detach that implication from `renamon`.
  - The effective parent graph should be computed from raw parent relationships
    plus sibling equivalence, with conflict/cycle checks before accepting changes.
- Sibling relationships should be rejected if their effective graph would create
  a parent cycle.

## Data Universes

Octans has three related but distinct views of tag data.

- The physical universe is the stored database reality.
  - These are the raw tag mappings, media records, and relationship rows that
    actually exist.
  - Explicit tag writes happen here.
  - Imported tags can be stored faithfully here even when they resolve to a
    canonical tag elsewhere.
- The semantic universe is the intended meaning of those facts.
  - Parent and sibling relationships are in force immediately as semantic facts.
  - This universe says how tags and media should relate once all implications are
    fully realized.
- The materialized universe is the derived/queryable state.
  - Cached, derived, or calculated canonicalization brings the physical universe
    closer to the semantic universe.
  - It may match the semantic universe, but it is not guaranteed to do so while
    relationship work is pending.
  - Queries operate against this universe.

This framing is the reason relationship changes can be semantically valid before
all search results reflect them. Parent/sibling relationships are created in the
semantic universe, then maintenance work gradually realizes them in the
materialized universe.

## Relationship State and Consistency

- Queries reflect the current materialized universe.
  - Query-time execution should not force complete canonicalization or parent
    rollout.
  - If `A -> B` exists but derived canonical state has not been rebuilt yet, a
    search for B is not required to find every file still represented only by raw
    A.
  - Query result counts should use the same current materialized state as query
    results, subject to ordinary caching rules.
  - Octans does not need Hydrus-style estimated sibling counts for now.
- Relationship creation should estimate and then either apply inline or enqueue
  maintenance work.
  - Creating a parent/sibling relationship saves a semantic relationship in the
    physical database.
  - Octans should quickly estimate how much work is needed to effectuate it.
  - If the affected count is tiny, such as around 5 items, the derived-state work
    can happen immediately.
  - If the affected count is larger, Octans should enqueue maintenance work and
    expose status such as pending, running, complete, or failed.
- New tag writes can try to update sibling/parent derived state synchronously as
  an optimization.
  - This is not guaranteed: a tag may imply many parents through long chains.
  - Maintenance/rebuild work remains the correctness backstop.
- The product expectation is best-effort/eventually-good relationship
  effectuation, not immediate global consistency.
  - Pending/running relationship work should be visible on a maintenance page,
    Hydrus-style.
  - Do not show global warning banners whenever relationship work is pending.
- Parent/sibling relationship rows do not need Hydrus-style status fields.
  - Octans does not need pending/petitioned repository semantics.
  - V1 can model active relationships only and delete rows when relationships are
    removed.
  - Rollout status belongs to maintenance/job state, not to relationship rows.

## Tag Display and Editing

- File tag reads should include direct and implied tags by default.
  - Normal detail display can show canonicalized tags.
  - Tag editing should expose raw/canonical information so the user understands
    what they are adding or removing.
  - Example: the editor can show that `character:spiderman` displays as
    `character:spider-man` rather than hiding the raw mapping completely.
- If a file has multiple raw mappings that resolve to one displayed canonical
  tag, display/search should collapse them for normal use.
- Removing such a displayed canonical tag is ambiguous.
  - The UI may ask which raw association the user means.
  - If a default is needed, remove all raw mappings that resolve to the displayed
    canonical tag.
  - Normal UI should default to removing all equivalent raw mappings.
  - Advanced tag editing can allow choosing a specific raw mapping.
- API removal should expose the choice explicitly rather than pretending it is
  obvious.
  - Likely modes: `allEquivalentRaw`, `specificRaw`, and perhaps
    `canonicalOnly`.

## Tag Writes and Imports

- Tag write APIs should report what was actually applied when practical.
  - If a client asks to add `character:spiderman`, and synchronous canonical
    lookup resolves it to `character:spider-man`, the response should be able to
    report requested/raw tag, effective/display tag, and whether canonicalization
    happened.
  - `canonicalized: false` may be a false negative if background
    canonicalization/cleanup later discovers or applies a sibling relationship.
  - The API contract should document that synchronous canonicalization reporting
    is best-effort and not proof that no sibling relationship exists.
- Manual UI/API writes should prefer canonical tags by default once a sibling
  relationship exists.
  - This is a policy for human-authored tags: help the user work with canonical
    form.
  - The UI/API should allow an explicit override to store the raw non-canonical
    tag.
  - The request model should expose canonicalization policy rather than hiding it
    as route magic.
  - Likely modes: `preferCanonical`, `preserveRaw`, and perhaps
    `rejectNonCanonical`.
- Imported/downloader-provided tags are trickier and may need configuration.
  - This is a different policy question because external tags carry source
    provenance.
  - Current lean: record external source tags faithfully, so imports store the
    actual tags received from the source even when a sibling exists.
  - Canonical display/search state can still make those imported tags behave like
    the canonical tag.

## Search API Shape

- Query endpoints should return frontend-neutral DTOs, not EF `HashItem` rows.
- Results should include enough to render a gallery tile, such as:
  - hash
  - original/media URL
  - thumbnail URL
  - content type
  - repository state
- Full tag lists do not need to be returned by default.
- Tags can be loaded through a separate API endpoint for selected/detail views.
- Search should always have an explicit sort order.
  - The default sort order should be import-time.
  - Pagination should be defined against the explicit sort order.
  - Use a deterministic tie-breaker, such as import-time plus hash ID.
- Search pagination should use limit/offset for now.
  - Cursor pagination can be added later if large-library infinite scrolling
    needs it.

## Remaining Open Questions

- What exact table/cache shape should materialized parent tags use?
- What exact table/cache shape should materialized sibling canonicalization use?
- How should relationship derived-state rebuilds be implemented safely: immediate
  transaction, background job, previewable maintenance action, or a combination?
- How should effective parent-relationship conflicts through sibling equivalence
  be represented beyond cycle rejection?
- What exact request/response shape should ambiguous canonical-tag removal use?
- Should imports always store raw source tags faithfully, or should that be
  configurable per importer/source?
