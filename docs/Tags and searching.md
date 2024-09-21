# Tag fundamentals

Tags are lowercase text strings describing a single property.
Tags are composed of a namespace and a subtag. These are called the tag's 'components'.
The subtag describes a property of what is being tagged; the namespace categorises the tag.
Namspaces precede the subtag with a colon.
An example is 'character:bayonetta'. 'bayonetta' is the subtag and 'character' is the namespace.

Every tag has a namespace. Subtags, when expressed by themselves, are actually part of a secret namespace which consists of an empty string, i.e. '[empty]:tag'.

Tags can contain whitespace in the namespace and subtag: 'character:samus aran' is valid, as is 'video game: tetris'.
Tags do not contain leading or trailing whitespace.
Tags never contain more than one consecutive instance of whitespace.
Namespaces and subtags cannot contain colons or asterisks, as this would lead to ambiguity.

# Searching

A query is composed of 1+ predicates.
A predicate states some fact about the files it will find.
Predicates can either be 'system predicates', 'tag predicates' or 'OR predicates'.

A predicate can be inclusive or exclusive. Inclusive predicates include the files they describe in their results; exclusive predicates exclude them. Inclusive predicates are the default. Exclusive predicates are represented by a prefixing hyphen. For instance, '-character:bayonetta' means to exclude all files which have the tag 'character:bayonetta'.

Multiple predicates in a query use AND logic by default: each predicate whittles down the results. A set intersection is performed on the results via all the predicates.

Tag predicates are composed via a free-text interface where the user directly enters tags. System and OR predicates are composed via a special non-text UI; in other words they have no textual representation which needs to be canonicalised via whitespace-trimming or lowercasing.

## System predicates
A system predicate interrogates file metadata, such as 'files imported in the last 24 hours' or 'files under a certain size'. System predicates are identified by a prefixing 'system' namespace, which is reserved from normal use. For instance, 'system: size < 50kb' will return all files smaller than 50kb.

## Tag predicates
Tag predicates interrogate the tags attached to files.

The simplest case is a direct tag search, e.g. 'character:bayonetta'. This returns all files which have this exact tag (this exact namespace/subtag combination).

As all tags are lowercase, all tag predicates are case-insensitive.

Tag predicates, like tags themselves, are trimmed of leading/trailing whitespace. All remaining consecutive instances of whitespace are reduced to one instance.

### Wildcards
Tag predicates can include wildcards, represented by the character *.

A tag component matches a wildcard if a contiguous sequence in the component can be elided to result in the wildcard, not including the * character:

- bayone* -> bayonetta, bayonet
- *hair -> red hair, blue hair
- b* eyes -> blue eyes, brown eyes

A wildcard can appear in both the namespace and subtag for a tag.
A wildcard can also totally replace either the namespace or subtag.

When a wildcard replaces the subtag component, e.g. 'namespace:*', it matches every tag in that namespace. 'character:*' will return files with 'character:bayonetta' and those with 'character:samus aran'.

When a wildcard replaces the namespace component, e.g. '*:subtag', it matches every tag with that subtag, no matter their namespace. '*:bayonetta' will return files with 'character:bayonetta' and 'series:bayonetta'.

If the namespace is specified but the subtag is partially wildcarded, e.g. 'character:bayo*', it matches every tag in that namespace where the subtag can be expanded out from the wildcarded subtag.

If the subtag is specified by the namespace is partially wildcarded, e.g. 'char*:bayonetta', it matches every tag which has that exact subtag and a namespace which can be expanded out from the wildcarded namespace.

If both the namespace and subtag are partially wildcarded, e.g. 'char*:bayo*', the matching process applies to both components. It matches every tag where the subtag can be expanded from the wildcarded subtag and is in any namespace which can be expanded from the wildcarded namespace.

If both the namespace and subtag are completely wildcarded, e.g. '*:*', it simply matches every single file.

## OR predicates
An OR predicate is a special kind of predicate. OR predicates are comprised of at least two other predicates. An OR predicate returns files which match at least one of the inner predicates. For example, 'character:bayonetta OR system:size < 50kb' will return any file which has that tag or is under that size.

An OR predicate cannot contain another OR predicate.

If an OR predicate is exclusive, it has 'neither' semantics. That is, '-(bayonetta OR samus aran)' means all tags which are neither 'bayonetta' or 'samus aran'. This case is trivially reducible to two separate non-OR predicates, i.e. '-bayonetta' AND '-samus aran'.
