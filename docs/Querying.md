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
- Natural language queries (maybe plug in an AI down the road)
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

!TODO: list out all the system queries intending to implement.

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
!TODO: ???

## Maintainability

### Unit Tests
- Parser edge cases
- Predicate combinations
- Optimization rules

### Integration Tests
- End-to-end query execution
- Performance benchmarks
- Concurrency scenarios

### Performance Tests
- Large dataset benchmarks
- Memory usage monitoring
- Query optimization effectiveness

### Performance Metrics
- Query execution time
- Cache hit rates
- Memory usage patterns
- Query pattern frequency

### Error Handling
- Clear error messages for users
- Detailed logging for debugging
- Graceful degradation under load

### Documentation
- Maintain this specification
- Document optimization rules
- Keep example queries updated
- Include performance tuning guide