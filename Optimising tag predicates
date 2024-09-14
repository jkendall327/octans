Optimising tag predicates

Exact Duplicates:

Remove any duplicate tag predicates.
Example: character:samus AND character:samus -> character:samus


Wildcard Subsumption:

If a wildcard predicate fully encompasses another predicate, remove the more specific one.
Example: character:* AND character:samus -> character:*


Exclusive vs Inclusive:

If there's an inclusive and exclusive predicate for the same tag, the query always returns an empty set.
Example: character:samus AND -character:samus -> (empty set)


Wildcard Conflicts:

Identify when a wildcard exclusive predicate negates an inclusive predicate.
Example: character:samus AND -character:s* -> (empty set)



System Predicates

Exact Duplicates:

Remove duplicate system predicates.


Logical Subsumption:

Identify when one system predicate logically implies another.
Example: system:size < 50kb AND system:size < 100kb -> system:size < 50kb


Mutually Exclusive Conditions:

Identify when two system predicates can never be true simultaneously.
Example: system:size < 50kb AND system:size > 100kb -> (empty set)



OR Predicates

Duplicate Elimination:

Remove duplicate predicates within an OR clause.
Example: (A OR B OR A) -> (A OR B)


Subsumption in OR:

If one predicate in an OR clause implies another, remove the implied predicate.
Example: (character:* OR character:samus) -> character:*


OR-AND Reduction:

If an OR predicate contains all possible values for a tag, it can be eliminated.
Example: (character:samus OR character:bayonetta) AND character:* -> character:*


Exclusive OR Simplification:

Simplify exclusive OR predicates when possible.
Example: -(A OR B) is equivalent to -A AND -B



Cross-Type Redundancy

System-Tag Interactions:

Identify when a system predicate makes a tag predicate redundant or vice versa.
Example: system:filetype=image AND character:* might be redundant if all character-tagged files are images.


OR-AND Interactions:

Simplify queries where an OR predicate is combined with one of its components.
Example: (A OR B) AND A -> A



Implementation Considerations

Predicate Normalization:

Convert predicates to a standard form for easier comparison.
Example: Expand wildcards to a regex-like format.


Logical Analysis:

Implement a system for logical comparison of predicates.
Consider using a constraint solver or symbolic logic system for complex cases.


Optimization Order:

Apply simplifications in a specific order, as some simplifications may enable others.
Iterate through simplifications until no further reductions are possible.


Performance Tradeoff:

Balance the cost of redundancy analysis against the performance gain in query execution.
For very simple queries, the overhead of analysis might exceed the benefits.
