using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using Octans.Core;
using Octans.Core.Models;

public class TagUpdater
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public TagUpdater(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> UpdateTags(UpdateTagsRequest request)
    {
        await using var connection = _connectionFactory.GetConnection();
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
        WHERE (Namespaces.Value = @Namespace OR (@Namespace IS NULL AND Namespaces.Value = ''))
          AND Subtags.Value = @Subtag
    )";

        foreach (var tag in tagsToRemove)
        {
            await connection.ExecuteAsync(query, new 
            { 
                HashId = hashId, 
                Namespace = tag.Namespace, 
                Subtag = tag.Subtag 
            });
        }
    }

    private async Task AddTags(IDbConnection connection, IList<TagModel> tagsToAdd, int hashId)
    {
        var upsertNamespace = @"
    INSERT OR IGNORE INTO Namespaces (Value) 
    VALUES (@Value);
    SELECT Id FROM Namespaces WHERE Value = @Value;";

        var upsertSubtag = @"
    INSERT OR IGNORE INTO Subtags (Value) 
    VALUES (@Value);
    SELECT Id FROM Subtags WHERE Value = @Value;";

        var upsertTag = @"
    INSERT OR IGNORE INTO Tags (NamespaceId, SubtagId) 
    VALUES (@NamespaceId, @SubtagId);
    SELECT Id FROM Tags WHERE NamespaceId = @NamespaceId AND SubtagId = @SubtagId;";

        var insertMapping = @"
    INSERT OR IGNORE INTO Mappings (TagId, HashId)
    VALUES (@TagId, @HashId);";

        foreach (var tag in tagsToAdd)
        {
            // Upsert namespace
            var namespaceId = await connection.QuerySingleAsync<int>(upsertNamespace, new { Value = tag.Namespace ?? string.Empty });

            // Upsert subtag
            var subtagId = await connection.QuerySingleAsync<int>(upsertSubtag, new { Value = tag.Subtag });

            // Upsert tag
            var tagId = await connection.QuerySingleAsync<int>(upsertTag, new { NamespaceId = namespaceId, SubtagId = subtagId });

            // Insert mapping
            await connection.ExecuteAsync(insertMapping, new { TagId = tagId, HashId = hashId });
        }
    }
}