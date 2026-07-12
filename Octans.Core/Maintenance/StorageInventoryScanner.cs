using System.IO.Abstractions;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Octans.Core.Filesystem;
using Octans.Data.Models;
using Octans.Data.Models.Maintenance;

namespace Octans.Core.Maintenance;

internal sealed class StorageInventoryScanner(
    IDbContextFactory<ServerDbContext> factory,
    IFileSystem fileSystem,
    IOptions<GlobalSettings> settings,
    ImageStorage imageStorage)
{
    private const int MetadataProbeSize = 256 * 1024;
    private readonly string _filesRoot = fileSystem.Path.Join(settings.Value.AppRoot, "db", "files");

    public async Task<StorageScanSummary> ScanAsync(
        Func<StorageMaintenanceFinding, CancellationToken, Task> recordFinding,
        Func<StorageScanProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var storedItems = await db.Hashes
            .AsNoTracking()
            .Where(h => h.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var files = EnumerateStorageFiles().ToList();
        var totalItems = storedItems.Count + files.Count;
        var processed = 0;
        var scannedBytes = files.Sum(f => f.Size);
        var findings = 0;

        async Task Add(StorageMaintenanceFinding finding)
        {
            findings++;
            await recordFinding(finding, cancellationToken);
        }

        foreach (var invalid in files.Where(f => f.Kind is StorageFileKind.Malformed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Add(NewFinding(
                StorageFindingType.MalformedStorageFile,
                StorageFindingSeverity.Warning,
                null,
                invalid.Path,
                null,
                invalid.Size,
                "The file does not follow the content-store bucket and filename conventions."));
            processed++;
        }

        var originals = files
            .Where(f => f.Kind is StorageFileKind.Original)
            .GroupBy(f => f.Hash!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Path, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);
        var thumbnails = files
            .Where(f => f.Kind is StorageFileKind.Thumbnail)
            .GroupBy(f => f.Hash!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Path, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);
        var activeHashes = storedItems
            .Select(h => Convert.ToHexString(h.Hash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in storedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = ContentHash.FromHashBytes(item.Hash);
            var hex = hash.Hex;
            var expectedOriginal = item.Extension is null
                ? null
                : imageStorage.GetOriginalDestination(hash, new(item.Extension, item.ContentType ?? "application/octet-stream"));
            var expectedThumbnail = imageStorage.GetThumbnailDestination(hash);

            if (!originals.TryGetValue(hex, out var matchingOriginals))
            {
                await Add(NewFinding(
                    StorageFindingType.MissingOriginal,
                    StorageFindingSeverity.Error,
                    hex,
                    null,
                    expectedOriginal,
                    null,
                    "The database record has no original file on disk."));
            }
            else
            {
                var canonical = SelectCanonical(matchingOriginals, expectedOriginal);
                if (expectedOriginal is not null && !PathsEqual(canonical.Path, expectedOriginal))
                {
                    await Add(NewFinding(
                        StorageFindingType.MisplacedOriginal,
                        StorageFindingSeverity.Warning,
                        hex,
                        canonical.Path,
                        expectedOriginal,
                        canonical.Size,
                        "The original is stored outside its deterministic location."));
                }

                foreach (var duplicate in matchingOriginals.Where(f => !PathsEqual(f.Path, canonical.Path)))
                {
                    await Add(NewFinding(
                        StorageFindingType.DuplicateOriginal,
                        StorageFindingSeverity.Warning,
                        hex,
                        duplicate.Path,
                        expectedOriginal,
                        duplicate.Size,
                        "More than one physical original exists for this content hash."));
                }

                await InspectOriginal(item, canonical, expectedOriginal, Add, cancellationToken);
            }

            if (!thumbnails.TryGetValue(hex, out var matchingThumbnails))
            {
                await Add(NewFinding(
                    StorageFindingType.MissingThumbnail,
                    StorageFindingSeverity.Warning,
                    hex,
                    null,
                    expectedThumbnail,
                    null,
                    "The media item has no thumbnail on disk."));
            }
            else
            {
                var canonical = SelectCanonical(matchingThumbnails, expectedThumbnail);
                if (!PathsEqual(canonical.Path, expectedThumbnail))
                {
                    await Add(NewFinding(
                        StorageFindingType.MisplacedThumbnail,
                        StorageFindingSeverity.Warning,
                        hex,
                        canonical.Path,
                        expectedThumbnail,
                        canonical.Size,
                        "The thumbnail is stored outside its deterministic location."));
                }

                foreach (var duplicate in matchingThumbnails.Where(f => !PathsEqual(f.Path, canonical.Path)))
                {
                    await Add(NewFinding(
                        StorageFindingType.DuplicateThumbnail,
                        StorageFindingSeverity.Warning,
                        hex,
                        duplicate.Path,
                        expectedThumbnail,
                        duplicate.Size,
                        "More than one thumbnail exists for this content hash."));
                }
            }

            processed++;
            await reportProgress(new(totalItems, processed, findings, scannedBytes, hex), cancellationToken);
        }

        foreach (var file in files.Where(f => f.Kind is StorageFileKind.Original or StorageFileKind.Thumbnail))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activeHashes.Contains(file.Hash!))
            {
                processed++;
                continue;
            }

            var type = file.Kind is StorageFileKind.Original
                ? StorageFindingType.OrphanedOriginal
                : StorageFindingType.OrphanedThumbnail;
            await Add(NewFinding(
                type,
                StorageFindingSeverity.Warning,
                file.Hash,
                file.Path,
                null,
                file.Size,
                "The physical file has no active database record."));
            processed++;
            await reportProgress(new(totalItems, processed, findings, scannedBytes, file.Path), cancellationToken);
        }

        await reportProgress(new(totalItems, processed, findings, scannedBytes, null), cancellationToken);
        return new(totalItems, processed, findings, scannedBytes);
    }

    private async Task InspectOriginal(
        HashItem item,
        StorageFile file,
        string? expectedPath,
        Func<StorageMaintenanceFinding, Task> add,
        CancellationToken cancellationToken)
    {
        await using var stream = fileSystem.File.OpenRead(file.Path);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var expectedHash = Convert.ToHexString(item.Hash);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            await add(NewFinding(
                StorageFindingType.ContentHashMismatch,
                StorageFindingSeverity.Error,
                expectedHash,
                file.Path,
                expectedPath,
                file.Size,
                $"The file content hashes to {actualHash}, not the hash recorded in its filename and database row."));
            return;
        }

        var extension = fileSystem.Path.GetExtension(file.Path).TrimStart('.').ToLowerInvariant();
        var physicalExtensionMismatch = !string.Equals(
            extension,
            item.Extension?.TrimStart('.'),
            StringComparison.OrdinalIgnoreCase);
        if (physicalExtensionMismatch)
        {
            await add(NewFinding(
                StorageFindingType.ExtensionMismatch,
                StorageFindingSeverity.Warning,
                expectedHash,
                file.Path,
                expectedPath,
                file.Size,
                $"The database extension '{item.Extension ?? "<missing>"}' does not match the physical extension '{extension}'."));
        }

        try
        {
            stream.Position = 0;
            var probe = new byte[Math.Min(MetadataProbeSize, checked((int)Math.Min(file.Size, int.MaxValue)))];
            var read = await stream.ReadAtLeastAsync(probe, probe.Length, throwOnEndOfStream: false, cancellationToken);
            var metadata = imageStorage.GetMetadata(probe.AsSpan(0, read).ToArray());
            if (!physicalExtensionMismatch &&
                !string.Equals(metadata.Extension, item.Extension?.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
            {
                await add(NewFinding(
                    StorageFindingType.ExtensionMismatch,
                    StorageFindingSeverity.Warning,
                    expectedHash,
                    file.Path,
                    expectedPath,
                    file.Size,
                    $"The detected extension '.{metadata.Extension}' differs from '.{item.Extension ?? "<missing>"}'."));
            }

            if (!string.Equals(metadata.ContentType, item.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                await add(NewFinding(
                    StorageFindingType.ContentTypeMismatch,
                    StorageFindingSeverity.Warning,
                    expectedHash,
                    file.Path,
                    expectedPath,
                    file.Size,
                    $"The detected content type '{metadata.ContentType}' differs from '{item.ContentType ?? "<missing>"}'."));
            }
        }
        catch
        {
            // Hash integrity remains useful even when the MIME detector cannot classify the content.
        }
    }

    private IEnumerable<StorageFile> EnumerateStorageFiles()
    {
        if (!fileSystem.Directory.Exists(_filesRoot))
        {
            yield break;
        }

        foreach (var file in fileSystem.DirectoryInfo.New(_filesRoot).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var parent = file.Directory?.Name ?? string.Empty;
            var name = fileSystem.Path.GetFileNameWithoutExtension(file.Name);
            var type = parent.Length == 3 ? char.ToLowerInvariant(parent[0]) : '\0';
            var bucket = parent.Length == 3 ? parent[1..].ToLowerInvariant() : string.Empty;
            var validBucket = bucket.Length == 2 && bucket.All(Uri.IsHexDigit);
            var validHash = name.Length == 64 && name.All(Uri.IsHexDigit);
            var expectedBucket = validHash ? name[..2].ToLowerInvariant() : string.Empty;
            var kind = type switch
            {
                'f' when validBucket && validHash => StorageFileKind.Original,
                't' when validBucket && validHash => StorageFileKind.Thumbnail,
                _ => StorageFileKind.Malformed
            };

            yield return new(
                file.FullName,
                validHash ? name.ToUpperInvariant() : null,
                kind,
                file.Length,
                validBucket && validHash && bucket == expectedBucket);
        }
    }

    private StorageFile SelectCanonical(IReadOnlyList<StorageFile> files, string? expectedPath) =>
        expectedPath is null
            ? files[0]
            : files.FirstOrDefault(f => PathsEqual(f.Path, expectedPath)) ?? files[0];

    private bool PathsEqual(string left, string right) =>
        string.Equals(
            fileSystem.Path.GetFullPath(left),
            fileSystem.Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static StorageMaintenanceFinding NewFinding(
        StorageFindingType type,
        StorageFindingSeverity severity,
        string? hash,
        string? path,
        string? expectedPath,
        long? size,
        string message) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Severity = severity,
        Hash = hash,
        Path = path,
        ExpectedPath = expectedPath,
        Size = size,
        Message = message,
        Resolution = type is StorageFindingType.MissingOriginal
            ? StorageFindingResolution.NotRepairable
            : StorageFindingResolution.Open
    };

    private sealed record StorageFile(
        string Path,
        string? Hash,
        StorageFileKind Kind,
        long Size,
        bool IsInExpectedBucket);

    private enum StorageFileKind
    {
        Original,
        Thumbnail,
        Malformed
    }
}

internal sealed record StorageScanProgress(
    int TotalItems,
    int ProcessedItems,
    int Findings,
    long ScannedBytes,
    string? CurrentItem);

internal sealed record StorageScanSummary(
    int TotalItems,
    int ProcessedItems,
    int Findings,
    long ScannedBytes);
