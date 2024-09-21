# the main query method - GetHashIdsFromQuery
get all the system predicates

if we have a limit predicate, and the limit is zero, return nothing

there_are_tags_to_search = 
len( tags_to_include ) > 0 or 
len(namespaces_to_include ) > 0 or 
len( wildcards_to_include ) > 0

get any simple file info predicates (size, mime types, etc)

if there are ORs to do and there are no tags to search for and no simple file info predicates
self._DoOrPreds

if 'hash' in simple_preds:
...

do timestamp predicates
do simple rating predicates
other system predicates

## tags pt. 1

sort the tags to include by ( 1 if HydrusTags.IsUnnamespaced( s ) else 0, -len( s ) )

for tag in tags_to_include:
    if query_hash_ids is None:
        tag_query_hash_ids = self.modules_files_search_tags.GetHashIdsFromTag
    elif is_inbox and len( query_hash_ids ) == len( self.modules_files_inbox.inbox_hash_ids )
        tag_query_hash_ids = self.modules_files_search_tags.GetHashIdsFromTag
    else:
        with self._MakeTemporaryIntegerTable( query_hash_ids, 'hash_id' ) as temp_table_name...

    intersection_update_qhi

    if len( query_hash_ids ) == 0: return []

sort namespaces to include by ( key = lambda n: -len( n ) )

for namespace in namespaces_to_include:
    if query_hash_ids is None or ( is_inbox and len( query_hash_ids ) == len( self.modules_files_inbox.inbox_hash_ids ) ):
        namespace_query_hash_ids = self.modules_files_search_tags.GetHashIdsThatHaveTagsComplexLocation
    else:
        with self._MakeTemporaryIntegerTable ...
        namespace_query_hash_ids = self.modules_files_search_tags.GetHashIdsThatHaveTagsComplexLocation
    
    intersection_update_qhi
    
    if len( query_hash_ids ) == 0: return []

sort wildcards to include: key = lambda w: -len( w )

for wildcard in wildcards_to_include:
    self.modules_files_search_tags.GetHashIdsFromWildcardComplexLocation

    etc etc

## ORs pt. 2 and simple preds

// OR round two--if file preds will not be fast, let's step in to reduce the file domain search space

if not done_or_predicates and not there_are_simple_files_info_preds_to_search_for:
    self._DoOrPreds

we_need_some_results = query_hash_ids is None

if we_need_some_results:
    [some weird stuff here i don't understand]

## working on stuff in memory
if not done_tricky_incdec_ratings:
if 'hash' in simple_preds:
if 'has_exif' in simple_preds:
...

// OR round three--final chance to kick in, and the preferred one. query_hash_ids is now set, so this shouldn't be super slow for most scenarios
if not done_or_predicates:
    query_hash_ids = self._DoOrPreds

// now subtract bad results
if len( tags_to_exclude ) + len( namespaces_to_exclude ) + len( wildcards_to_exclude ) > 0:
    for tag in tags_to_exclude:
        unwanted_hash_ids = self.modules_files_search_tags.GetHashIdsFromTag
        query_hash_ids.difference_update( unwanted_hash_ids )
    for namespace in namespaces_to_exclude:
        ...
    for wildcard in wildcards_to_exclude:
        ...

[other system-style predicates here like duplicates, urls, ratings, tag numbers...]

## final cleanup

query_hash_ids = list( query_hash_ids )
we_are_applying_limit = system_limit is not None and system_limit < len( query_hash_ids )

[apply any limits and apply a sort if one is desired]
self.TryToSortHashIds

# DoOrPreds

# GetHashIdsFromTag (tags to include)

if not self.modules_tags.TagExists( tag ):
    return set()

( namespace, subtag ) = HydrusTags.SplitTag( tag )

some_results = self.GetHashIdsFromTagIds()...

# GetHashIdsThatHaveTagsComplexLocation (namespaces to include)

# GetHashIdsFromWildcardComplexLocation (wildcards to include)

( namespace_wildcard, subtag_wildcard ) = HydrusTags.SplitTag( wildcard )

// i.e. 'character:*'
if subtag_wildcard == '*':
    return self.GetHashIdsThatHaveTagsComplexLocation(...)

if namespace_wildcard == '*':
    possible_namespace_ids = []
else:
    possible_namespace_ids = self.modules_tag_search.GetNamespaceIdsFromWildcard( namespace_wildcard )

with self._MakeTemporaryIntegerTable( possible_namespace_ids)...
    if namespace_wildcard == '*':
        namespace_ids_table_name = None
    else:
        namespace_ids_table_name = temp_namespace_ids_table_name

some_results = self.GetHashIdsFromWildcardSimpleLocation( ..., subtag_wildcard, namespace_ids_table_name = namespace_ids_table_name, ... )

# GetHashIdsFromWildcardSimpleLocation

with self._MakeTemporaryIntegerTable( [], 'subtag_id' ) as temp_subtag_ids_table_name:
    self.modules_tag_search.GetSubtagIdsFromWildcardIntoTable

    if namespace_ids_table_name is None:
        return self.GetHashIdsFromSubtagIdsTable
    else:
        return self.GetHashIdsFromNamespaceIdsSubtagIdsTables

# GetHashIdsFromTagIds (the core method?)

# GetNamespaceIdsFromWildcard
        if namespace_wildcard == '*':
            return self._STL( self._Execute( 'SELECT namespace_id FROM namespaces;' ) )
        elif '*' in namespace_wildcard:
            like_param = ConvertWildcardToSQLiteLikeParameter( namespace_wildcard )
            return self._STL( self._Execute( 'SELECT namespace_id FROM namespaces WHERE namespace LIKE ?;', ( like_param, ) ) )
        else:
            if self.modules_tags.NamespaceExists( namespace_wildcard ):
                namespace_id = self.modules_tags.GetNamespaceId( namespace_wildcard )
                return [ namespace_id ]
            else:
                return []

# GetSubtagIdsFromWildcard

if '*' in subtag_wildcard:
    wildcard_has_fts4_searchable_characters = WildcardHasFTS4SearchableCharacters( subtag_wildcard )

    if subtag_wildcard == '*':
        query = 'SELECT docid FROM {};'.format( subtags_fts4_table_name )

elif ClientSearch.IsComplexWildcard( subtag_wildcard ) or not wildcard_has_fts4_searchable_characters:
    like_param = ConvertWildcardToSQLiteLikeParameter( subtag_wildcard )

    ...

