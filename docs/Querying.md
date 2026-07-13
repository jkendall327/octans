# Octans Query System Specification

## Overview
Octans lets users search for images using tag-based predicates and system attributes. 

This document assumes conceptual familiarity with tags.

Goals:
- Simple to use, limited grammar of AND, NOT and OR logic (which can be nested or combined)
- Performant at scale (hundreds of thousands of image-tag mappings, sub-second response for simple queries)
- Well-specified, cohesive, maintainable implementation

Anti-goals:
- Maximum flexibility
- Making everything dynamic/not hardcoding things to make them easier
- Natural language queries
- Complex boolean expressions beyond AND/OR/NOT

## Searching
Tags matter because they are the primary way users search for images.

A search is composed of one or more predicates.
A predicate is either a tag query or a system query.
System queries are pre-defined and hardcoded.

## The query grammar

### Basic syntax
- Simple tag query: `namespace:subtag`
- Namespace is optional: `subtag` is equivalent to `:subtag`
- Wildcards: `namespace:*` or `*:subtag` or `namespace:sub*` or `*:*`
- Negation: `-namespace:subtag`
- OR queries: `or:tag1 OR tag2`
- Nested OR queries: `or:tag1 OR (or:tag2 OR tag3)`
- Predicate combinations: `namespace:subtag1 subtag2 -subtag3`

Negation queries can also be wildcarded, e.g. `-character:mar*`.
A tag can have multiple wildcards: `character:s*mu* -> character: samus`.

### System predicates
System predicates query file attributes rather than tags:
- `system:filesize > 1MB`
- `system:width > 1920`
- `system:height > 1080`
- `system:tag_count > 5`
- `system:imported_after 2024-01-01`

System predicates can be combined with tag queries.

## The query pipeline

1. **Parsing**
    - Converts raw string input into structured predicates
    - Rejects invalid syntax but not meaningless queries
    - Validates syntax and basic semantics
    - Creates composition of basic predicates

2. **Planning**
    - Optimizes query structure
    - Identifies opportunities for short-circuiting
    - Applies query plan caching for common patterns
    - Potential goal of caching query plans

3. **Execution**
    - Translates predicates into SQL queries
    - Potential goals: batching, pagination, result caching

## Examples

### Simple Queries
```
character:mario              # Find images tagged with mario in character namespace
-character:bowser           # Exclude images tagged with character:bowser
character:*                 # Find all images with the character namespace
*:mario                 # Find all images with the character subtag (includes game:mario, character:mario, series:mario, etc.)
```

### Complex Queries
```
or:character:mario OR character:luigi  # Find images of either Mario or Luigi
character:mario -game:mario64         # Mario images, excluding those from Mario 64
or:character:mario OR (or:stage:1-1 OR stage:1-2)  # Nested OR example
```

A query comprising solely of OR predicates and nested OR predicates is equivalent to the non-nested version of that query.
The above example, for instance, is equivalent to `or:character:mario OR stage:1-1 OR stage:1-2`.

### System Queries

Query v1 supports:

- `system:everything` for the normal non-trash library scope.
- `system:inbox` for inbox media.
- `system:archive` for archived media.
- `system:trash` for trashed media. This is the predicate that explicitly opens
  trash scope.

System predicates are ordinary predicates. For example,
`system:everything character:samus` still requires the tag, and
`or:system:trash OR character:samus` matches trash plus normally scoped Samus
media.

Filesize, dimensions, tag count, import date, file type, and other media
predicates are intentionally outside query v1.

## Implementation

### Query parser
- Use clear error messages for syntax errors
- Support extensible predicate types
- Maintain parser simplicity - avoid complex grammar rules

### Query planner
- This can be a black box so long as it follows the as-if rule
- Specific query plan optimizations are not part of the public API
- Should still keep optimization rules simple and documented...

### Query executor

The v1 executor translates the predicate tree into composable EF/SQL filters.
Top-level predicates use AND semantics. OR nodes retain their nested structure,
and negative tag predicates become `NOT EXISTS`-style filters rather than
mutating the positive tag set.

Exact and wildcard tag matching is case-insensitive. Matching a parent tag also
matches mappings to its materialized descendant tags. Normal queries exclude
trash unless the tree contains an explicit `system:trash` predicate.

## HTTP API

`POST /api/files/query` accepts a stable paged request:

```json
{
  "predicates": ["character:samus", "-series:smash"],
  "offset": 0,
  "limit": 100
}
```

The limit must be between 1 and 500. Results contain `items`, `total`, `offset`,
and `limit`; items are frontend-neutral media DTOs rather than EF entities.
Results use ascending media ID as the stable v1 order.

Invalid queries return HTTP 400 with an `errors` array. Each error has a stable
code, message, predicate index, start offset, and length. The source range is
relative to the corresponding string in `predicates`.

`GET /api/query/suggestions?search=...&limit=...` returns both tag and system
predicate suggestions. Negative tag input retains its `-` prefix in suggested
values.

## Deliberate v1 Limits

- OR is explicit through the `or:... OR ...` form; whitespace does not imply OR.
- Negation applies to tag predicates. Negated system predicates and negated OR
  groups are rejected with structured errors.
- Sorting is fixed to ascending media ID.
- Sibling canonicalization depends on future materialized sibling state and is
  not performed live by this executor.
- Query-plan performance optimization and plan caching are future work. The
  planner currently performs only semantics-preserving structural deduplication.
