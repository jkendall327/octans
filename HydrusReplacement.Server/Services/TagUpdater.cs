using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Text;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;

public class TagUpdater
{
    private readonly string _connectionString;

    public TagUpdater(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("db.sqlite");
    }

    public async Task<bool> UpdateTags(UpdateTagsRequest request)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = connection.BeginTransaction();

        try
        {
            var hash = await GetHash(connection, request.HashId);
            if (hash == null)
            {
                return false;
            }

            await RemoveTags(connection, request.TagsToRemove, request.HashId);
            await AddTags(connection, request.TagsToAdd, request.HashId);

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
            await connection.ExecuteAsync(query, new { HashId = hashId, tag.Namespace, tag.Subtag });
        }
    }

    private async Task AddTags(IDbConnection connection, IEnumerable<TagModel> tagsToAdd, int hashId)
    {
        var upsertNamespace = @"
            INSERT INTO Namespaces (Value) VALUES (@Value)
            ON CONFLICT(Value) DO UPDATE SET Value = @Value
            RETURNING Id";

        var upsertSubtag = @"
            INSERT INTO Subtags (Value) VALUES (@Value)
            ON CONFLICT(Value) DO UPDATE SET Value = @Value
            RETURNING Id";

        var upsertTag = @"
            INSERT INTO Tags (NamespaceId, SubtagId) VALUES (@NamespaceId, @SubtagId)
            ON CONFLICT(NamespaceId, SubtagId) DO UPDATE SET NamespaceId = @NamespaceId
            RETURNING Id";

        var insertMapping = @"
            INSERT INTO Mappings (TagId, HashId)
            SELECT @TagId, @HashId
            WHERE NOT EXISTS (
                SELECT 1 FROM Mappings
                WHERE TagId = @TagId AND HashId = @HashId
            )";

        var sb = new StringBuilder();
        foreach (var tag in tagsToAdd)
        {
            var namespaceId = await connection.ExecuteScalarAsync<int>(upsertNamespace, new { Value = tag.Namespace ?? string.Empty });
            var subtagId = await connection.ExecuteScalarAsync<int>(upsertSubtag, new { Value = tag.Subtag });
            var tagId = await connection.ExecuteScalarAsync<int>(upsertTag, new { NamespaceId = namespaceId, SubtagId = subtagId });
            
            await connection.ExecuteAsync(insertMapping, new { TagId = tagId, HashId = hashId });
        }
    }
}