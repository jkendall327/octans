using System.Numerics;
using CoenM.ImageHash;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Filesystem;
using Octans.Data.Models;
using Octans.Data.Models.Duplicates;

namespace Octans.Core.Duplicates;

public sealed class DuplicateService(
    ServerDbContext context,
    IPerceptualHashProvider hashProvider,
    ImageStorage imageStorage,
    FileDeleter fileDeleter,
    TimeProvider timeProvider,
    ILogger<DuplicateService> logger)
{
    private const double CandidateSimilarityThreshold = 95.0;
    private const int PerceptualHashBitLength = sizeof(ulong) * 8;
    private static readonly int CandidateDistanceThreshold = SimilarityThresholdToMaxDistance(CandidateSimilarityThreshold);

    public async Task<int> CalculateMissingHashes(CancellationToken cancellationToken = default)
    {
        var hashes = await context.Hashes
            .Where(h => h.PerceptualHash == null && h.DeletedAt == null)
            .Take(100) // Process in batches
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var hashItem in hashes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = ContentHash.FromHashBytes(hashItem.Hash);
            var file = imageStorage.FindOriginal(hash, hashItem.Extension);

            if (file is not { Exists: true })
            {
                logger.LogWarning("File not found for hash {HashId}", hashItem.Id);
                continue;
            }

            try
            {
                await using var stream = file.OpenRead();
                hashItem.PerceptualHash = await hashProvider.GetHash(stream, cancellationToken);
                count++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to calculate perceptual hash for {HashId}", hashItem.Id);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return count;
    }

    public async Task<int> FindDuplicates(CancellationToken cancellationToken = default)
    {
        var hashRows = await context.Hashes
            .AsNoTracking()
            .Where(h => h.PerceptualHash != null && h.DeletedAt == null)
            .Select(h => new { h.Id, Hash = h.PerceptualHash!.Value })
            .ToListAsync(cancellationToken);

        var ignoredPairs = await GetIgnoredPairs(cancellationToken);
        var index = new PerceptualHashIndex();

        var found = 0;
        foreach (var item in hashRows.Select(row => new HashRow(row.Id, row.Hash)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var match in index.FindWithinDistance(item.Hash, CandidateDistanceThreshold))
            {
                var pair = HashPair.Create(item.Id, match.Id);

                if (ignoredPairs.Contains(pair)) continue;

                var similarity = CompareHash.Similarity(item.Hash, match.Hash);

                if (similarity < CandidateSimilarityThreshold) continue;

                context.DuplicateCandidates.Add(new DuplicateCandidate
                {
                    HashId1 = pair.HashId1,
                    HashId2 = pair.HashId2,
                    Distance = similarity,
                    CreatedAt = timeProvider.GetUtcNow()
                });
                ignoredPairs.Add(pair);
                found++;
            }

            index.Add(item);
        }

        await context.SaveChangesAsync(cancellationToken);
        return found;
    }

    public async Task Resolve(
        int candidateId,
        DuplicateResolution resolution,
        int? keepHashId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await context.DuplicateCandidates
            .FirstOrDefaultAsync(c => c.Id == candidateId, cancellationToken);

        if (candidate == null) return;

        if (keepHashId.HasValue)
        {
            var deleteId = GetDeletedHashId(candidate, keepHashId.Value);
            var results = await fileDeleter.ProcessDeletion([deleteId]);

            var failed = results.FirstOrDefault(result => !result.Success);
            if (failed is not null)
            {
                throw new InvalidOperationException($"Failed to delete hash {failed.Id}: {failed.Error}");
            }

            var affectedCandidates = await context.DuplicateCandidates
                .Where(c => c.HashId1 == deleteId || c.HashId2 == deleteId)
                .ToListAsync(cancellationToken);

            context.DuplicateCandidates.RemoveRange(affectedCandidates);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var pair = HashPair.Create(candidate.HashId1, candidate.HashId2);
        var decision = new DuplicateDecision
        {
            HashId1 = pair.HashId1,
            HashId2 = pair.HashId2,
            Resolution = resolution,
            DecidedAt = timeProvider.GetUtcNow()
        };
        context.DuplicateDecisions.Add(decision);

        // Remove candidate
        context.DuplicateCandidates.Remove(candidate);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<HashSet<HashPair>> GetIgnoredPairs(CancellationToken cancellationToken)
    {
        var ignoredPairs = new HashSet<HashPair>();

        var decisions = await context.DuplicateDecisions
            .AsNoTracking()
            .Select(d => new { d.HashId1, d.HashId2 })
            .ToListAsync(cancellationToken);

        foreach (var decision in decisions)
        {
            ignoredPairs.Add(HashPair.Create(decision.HashId1, decision.HashId2));
        }

        var candidates = await context.DuplicateCandidates
            .AsNoTracking()
            .Select(c => new { c.HashId1, c.HashId2 })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            ignoredPairs.Add(HashPair.Create(candidate.HashId1, candidate.HashId2));
        }

        return ignoredPairs;
    }

    private static int GetDeletedHashId(DuplicateCandidate candidate, int keepHashId)
    {
        if (candidate.HashId1 == keepHashId)
        {
            return candidate.HashId2;
        }

        if (candidate.HashId2 == keepHashId)
        {
            return candidate.HashId1;
        }

        throw new ArgumentException(
            $"Hash {keepHashId} is not part of duplicate candidate {candidate.Id}.",
            nameof(keepHashId));
    }

    private static int SimilarityThresholdToMaxDistance(double threshold)
    {
        var differentBitPercentage = 100.0 - threshold;
        return (int)Math.Floor(PerceptualHashBitLength * differentBitPercentage / 100.0);
    }

    private readonly record struct HashRow(int Id, ulong Hash);

    private readonly record struct HashPair(int HashId1, int HashId2)
    {
        public static HashPair Create(int firstHashId, int secondHashId) =>
            firstHashId < secondHashId
                ? new(firstHashId, secondHashId)
                : new(secondHashId, firstHashId);
    }

    private sealed class PerceptualHashIndex
    {
        private Node? _root;

        public void Add(HashRow row)
        {
            if (_root is null)
            {
                _root = new(row);
                return;
            }

            var node = _root;
            while (true)
            {
                var distance = HammingDistance(row.Hash, node.Row.Hash);
                if (!node.Children.TryGetValue(distance, out var child))
                {
                    node.Children.Add(distance, new(row));
                    return;
                }

                node = child;
            }
        }

        public IEnumerable<HashRow> FindWithinDistance(ulong hash, int maxDistance)
        {
            if (_root is null)
            {
                yield break;
            }

            var pending = new Stack<Node>();
            pending.Push(_root);

            while (pending.TryPop(out var node))
            {
                var distance = HammingDistance(hash, node.Row.Hash);
                if (distance <= maxDistance)
                {
                    yield return node.Row;
                }

                foreach (var (childDistance, child) in node.Children)
                {
                    if (childDistance < distance - maxDistance || childDistance > distance + maxDistance) continue;

                    pending.Push(child);
                }
            }
        }

        private static int HammingDistance(ulong first, ulong second) =>
            BitOperations.PopCount(first ^ second);

        private sealed class Node(HashRow row)
        {
            public HashRow Row { get; } = row;
            public Dictionary<int, Node> Children { get; } = [];
        }
    }
}
