using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;

public class TagUpdater
{
    private readonly string _connectionString = "db.sqlite";

    public async Task<bool> UpdateTags(UpdateTagsRequest request)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var hash = await GetHash(connection, request.HashId);

        if (hash == null)
        {
            return false;
        }
        
        await using var transaction = connection.BeginTransaction();

        try
        {
            var removalChunked = request.TagsToRemove.Chunk(500);
            
            foreach (var tagModels in removalChunked)
            {
                await RemoveTags(connection, tagModels, request.HashId);
            }
            
            await AddTags(connection, request.TagsToAdd.ToList(), request.HashId);

            transaction.Commit();
            
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<HashItem?> GetHash(IDbConnection connection, int hashId)
    {
        return await connection.QuerySingleOrDefaultAsync<HashItem>(
            "SELECT * FROM Hashes WHERE Id = @HashId", new { HashId = hashId });
    }

    private async Task RemoveTags(IDbConnection connection, IEnumerable<TagModel> tagsToRemove, int hashId)
    {
        var query = @"
        DELETE FROM Mappings
        WHERE HashId = @HashId
        AND TagId IN (
            SELECT Tags.Id
            FROM Tags
            JOIN Namespaces ON Tags.NamespaceId = Namespaces.Id
            JOIN Subtags ON Tags.SubtagId = Subtags.Id
            WHERE (
                (Namespaces.Value = @Namespace OR (@Namespace IS NULL AND Namespaces.Value = ''))
                AND Subtags.Value = @Subtag
            )
            AND (Namespaces.Value, Subtags.Value) IN @TagPairs
        )";

        var tagPairs = tagsToRemove.Select(t => new { t.Namespace, t.Subtag }).ToList();

        await connection.ExecuteAsync(query, new { HashId = hashId, TagPairs = tagPairs });
    }

    private async Task AddTags(IDbConnection connection, IList<TagModel> tagsToAdd, int hashId)
    {
        var upsertNamespaces = @"
        INSERT INTO Namespaces (Value) 
        VALUES (@Value)
        ON CONFLICT(Value) DO UPDATE SET Value = Value
        RETURNING Id, Value";

        var upsertSubtags = @"
        INSERT INTO Subtags (Value) 
        VALUES (@Value)
        ON CONFLICT(Value) DO UPDATE SET Value = Value
        RETURNING Id, Value";

        var upsertTags = @"
        INSERT INTO Tags (NamespaceId, SubtagId) 
        VALUES (@NamespaceId, @SubtagId)
        ON CONFLICT(NamespaceId, SubtagId) DO UPDATE SET NamespaceId = NamespaceId
        RETURNING Id, NamespaceId, SubtagId";

        var insertMappings = @"
        INSERT INTO Mappings (TagId, HashId)
        SELECT t.Id, @HashId
        FROM (VALUES @TagIds) AS t(Id)
        WHERE NOT EXISTS (
            SELECT 1 FROM Mappings
            WHERE TagId = t.Id AND HashId = @HashId
        )";

        // Batch upsert namespaces
        var namespaces = tagsToAdd.Select(t => t.Namespace ?? string.Empty).Distinct().ToList();
        var upsertedNamespaces = await connection.QueryAsync<(int Id, string Value)>(upsertNamespaces, namespaces.Select(n => new { Value = n }));
        var namespaceDict = upsertedNamespaces.ToDictionary(x => x.Value, x => x.Id);

        // Batch upsert subtags
        var subtags = tagsToAdd.Select(t => t.Subtag).Distinct().ToList();
        var upsertedSubtags = await connection.QueryAsync<(int Id, string Value)>(upsertSubtags, subtags.Select(s => new { Value = s }));
        var subtagDict = upsertedSubtags.ToDictionary(x => x.Value, x => x.Id);

        // Batch upsert tags
        var tagPairs = tagsToAdd.Select(t => new
            {
                NamespaceId = namespaceDict[t.Namespace ?? string.Empty],
                SubtagId = subtagDict[t.Subtag]
            })
            .Distinct()
            .ToList();
        
        var tagIds = await connection.QueryAsync<int>(upsertTags, tagPairs);

        // Batch insert mappings
        await connection.ExecuteAsync(insertMappings, new { TagIds = tagIds, HashId = hashId });
    }
}