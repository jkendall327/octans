using CoenM.ImageHash;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Models;
using Octans.Core.Models.Duplicates;
using Octans.Server.Services;

namespace Octans.Core.Duplicates;

public class DuplicateService(
    ServerDbContext context,
    IPerceptualHashProvider hashProvider,
    SubfolderManager subfolderManager,
    FileDeleter fileDeleter,
    ILogger<DuplicateService> logger)
{
    public async Task<int> CalculateMissingHashes(CancellationToken cancellationToken = default)
    {
        var hashes = await context.Hashes
            .Where(h => h.PerceptualHash == null && h.DeletedAt == null)
            .Take(100) // Process in batches
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var hashItem in hashes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var hashed = HashedBytes.FromHashed(hashItem.Hash);
            var file = subfolderManager.GetFilepath(hashed);

            if (file == null || !file.Exists)
            {
                logger.LogWarning("File not found for hash {HashId}", hashItem.Id);
                continue;
            }

            try
            {
                // We need to use System.IO.Abstractions types if possible, but OpenRead returns Stream.
                // file is IFileSystemInfo, so we can cast to IFileInfo to use OpenRead.
                using var stream = ((System.IO.Abstractions.IFileInfo)file).OpenRead();
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
        // Simple O(N^2) comparison for now, can be optimized with BK-tree or similar later.
        // We only compare items that have a PerceptualHash.

        var items = await context.Hashes
            .Where(h => h.PerceptualHash != null && h.DeletedAt == null)
            .Select(h => new { h.Id, Hash = (ulong)h.PerceptualHash! })
            .ToListAsync(cancellationToken);

        int found = 0;

        // Existing decisions to skip
        var decisions = await context.DuplicateDecisions
            .Select(d => new { d.HashId1, d.HashId2 })
            .ToListAsync(cancellationToken);

        var decisionSet = new HashSet<(int, int)>();
        foreach (var d in decisions)
        {
            decisionSet.Add((Math.Min(d.HashId1, d.HashId2), Math.Max(d.HashId1, d.HashId2)));
        }

        // Existing candidates to skip
        var candidates = await context.DuplicateCandidates
            .Select(c => new { c.HashId1, c.HashId2 })
            .ToListAsync(cancellationToken);

        var candidateSet = new HashSet<(int, int)>();
        foreach (var c in candidates)
        {
            candidateSet.Add((Math.Min(c.HashId1, c.HashId2), Math.Max(c.HashId1, c.HashId2)));
        }

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var item1 = items[i];
                var item2 = items[j];

                var pair = (Math.Min(item1.Id, item2.Id), Math.Max(item1.Id, item2.Id));

                if (decisionSet.Contains(pair) || candidateSet.Contains(pair)) continue;

                var similarity = CompareHash.Similarity(item1.Hash, item2.Hash);

                // Similarity is 0-100. Let's say > 90 is a candidate?
                // Wait, CoenM.ImageHash uses Similarity (0-100).

                if (similarity >= 95.0) // Threshold can be adjustable
                {
                    context.DuplicateCandidates.Add(new DuplicateCandidate
                    {
                        HashId1 = pair.Item1,
                        HashId2 = pair.Item2,
                        Distance = similarity,
                        CreatedAt = DateTime.UtcNow
                    });
                    candidateSet.Add(pair); // Prevent re-adding in same loop
                    found++;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return found;
    }

    public async Task Resolve(int candidateId, DuplicateResolution resolution, int? keepHashId)
    {
        var candidate = await context.DuplicateCandidates
            .Include(c => c.Hash1)
            .Include(c => c.Hash2)
            .FirstOrDefaultAsync(c => c.Id == candidateId);

        if (candidate == null) return;

        // Create decision record
        var decision = new DuplicateDecision
        {
            HashId1 = candidate.HashId1,
            HashId2 = candidate.HashId2,
            Resolution = resolution,
            DecidedAt = DateTime.UtcNow
        };
        context.DuplicateDecisions.Add(decision);

        // Remove candidate
        context.DuplicateCandidates.Remove(candidate);

        if (resolution == DuplicateResolution.KeepBoth)
        {
            // Nothing else to do
        }
        else if (resolution == DuplicateResolution.Distinct)
        {
             // Nothing else to do (marked as distinct)
        }
        // else if (resolution == DuplicateResolution.Ignored) // Not used in UI yet?
        // {
             // Just skip
        // }

        // If we are keeping one, we delete the other.
        // But DuplicateResolution doesn't fully capture "Keep A" vs "Keep B".
        // The UI should pass which one to keep, or we handle "Delete" separately?
        // The prompt says: "Keep A, Keep B, both are equally good, skip".

        // If "Keep A", we resolve as... actually "Keep A" means we delete B.
        // So the pair is no longer a duplicate because B is deleted.
        // We probably don't need a DuplicateDecision in that case, because B is gone.
        // BUT if B is reimported, we might want to know?

        if (keepHashId.HasValue)
        {
            // We are keeping keepHashId, deleting the other.
            var deleteId = candidate.HashId1 == keepHashId.Value ? candidate.HashId2 : candidate.HashId1;

            // Delete the file
            await fileDeleter.ProcessDeletion(new[] { deleteId });

            // We don't save a DuplicateDecision if one is deleted, because the pair is broken.
            // Or maybe we should? If we delete B, and reimport B later, it will be a new HashItem (new ID? No, same ID if not hard deleted).
            // ReimportChecker reactivates existing hash.
            // So if we delete B, B.DeletedAt is set.
            // If B is reimported, B.DeletedAt is cleared.
            // Then FindDuplicates runs again. It sees A and B. It flags them again.
            // User sees them again.
            // So we SHOULD record that we resolved this pair by deleting one?
            // But if we deleted it, why did we reimport it? Maybe user made a mistake?

            // If I delete B, I don't want it to show up as a duplicate of A again immediately.
            // But it won't, because FindDuplicates filters out Deleted items.

            // So, deleting B is sufficient to remove the candidate.
            // But I should remove the Candidate record too.
            // Which I am doing.

            // BUT: If I delete B, do I add a Decision?
            // If I add a Decision (A, B, KeepBoth/Distinct), it might block future checks.
            // If I don't add a Decision, and B comes back, it's flagged again.
            // This seems correct. If B comes back, it's a "new" duplicate issue.

            // However, the function signature needs to handle this.
            // If keepHashId is provided, it implies deletion of the other.
            // So resolution might be irrelevant or implicit.
        }
        else
        {
            // KeepBoth or Distinct
            // Decision is added.
        }

        await context.SaveChangesAsync();
    }
}
