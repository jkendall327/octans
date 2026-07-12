using System.IO.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Octans.Core.Filesystem;
using Octans.Core.Thumbnails;
using Octans.Data.Models;
using Octans.Data.Models.Maintenance;

namespace Octans.Core.Maintenance;

internal sealed class StorageRepairer(
    ServerDbContext db,
    IFileSystem fileSystem,
    IOptions<GlobalSettings> settings,
    ImageStorage imageStorage,
    ThumbnailCreator thumbnailCreator)
{
    private readonly string _filesRoot = fileSystem.Path.GetFullPath(
        fileSystem.Path.Join(settings.Value.AppRoot, "db", "files"));
    private readonly string _quarantineRoot = fileSystem.Path.Join(
        settings.Value.AppRoot, "db", "maintenance", "quarantine");

    public static bool CanRepair(StorageFindingType type, StorageRepairActions actions) => type switch
    {
        StorageFindingType.MissingThumbnail => actions.HasFlag(StorageRepairActions.RegenerateMissingThumbnails),
        StorageFindingType.ExtensionMismatch or StorageFindingType.ContentTypeMismatch =>
            actions.HasFlag(StorageRepairActions.RepairMetadata),
        StorageFindingType.OrphanedOriginal or
            StorageFindingType.OrphanedThumbnail or
            StorageFindingType.MalformedStorageFile or
            StorageFindingType.DuplicateOriginal or
            StorageFindingType.DuplicateThumbnail or
            StorageFindingType.ContentHashMismatch or
            StorageFindingType.MisplacedOriginal or
            StorageFindingType.MisplacedThumbnail => actions.HasFlag(StorageRepairActions.QuarantineUnsafeFiles),
        _ => false
    };

    public static IReadOnlyList<StorageFindingType> GetRepairableTypes(StorageRepairActions actions) =>
        Enum.GetValues<StorageFindingType>().Where(type => CanRepair(type, actions)).ToList();

    public async Task<string> RepairAsync(
        StorageMaintenanceFinding finding,
        Guid repairJobId,
        CancellationToken cancellationToken)
    {
        return finding.Type switch
        {
            StorageFindingType.MissingThumbnail => await RegenerateThumbnail(finding, cancellationToken),
            StorageFindingType.ExtensionMismatch or StorageFindingType.ContentTypeMismatch =>
                await RepairMetadata(finding, cancellationToken),
            StorageFindingType.MisplacedOriginal or StorageFindingType.MisplacedThumbnail =>
                Relocate(finding),
            _ => Quarantine(finding, repairJobId)
        };
    }

    private async Task<string> RegenerateThumbnail(
        StorageMaintenanceFinding finding,
        CancellationToken cancellationToken)
    {
        var hash = ParseHash(finding);
        var item = await FindHashItem(hash, cancellationToken);
        var original = imageStorage.FindOriginal(hash, item.Extension)
            ?? throw new FileNotFoundException("The original required to regenerate this thumbnail is missing.");
        var bytes = await fileSystem.File.ReadAllBytesAsync(original.FullName, cancellationToken);
        await thumbnailCreator.ProcessThumbnailRequestAsync(
            new ThumbnailCreationRequest(bytes, hash),
            cancellationToken);
        return $"Regenerated thumbnail at {imageStorage.GetThumbnailDestination(hash)}.";
    }

    private async Task<string> RepairMetadata(
        StorageMaintenanceFinding finding,
        CancellationToken cancellationToken)
    {
        var hash = ParseHash(finding);
        var item = await FindHashItem(hash, cancellationToken);
        var original = FindExistingPath(finding) ?? imageStorage.FindOriginal(hash, item.Extension)?.FullName
            ?? throw new FileNotFoundException("The original required to repair metadata is missing.");
        var bytes = await fileSystem.File.ReadAllBytesAsync(original, cancellationToken);
        var metadata = imageStorage.GetMetadata(bytes);
        item.Extension = metadata.Extension;
        item.ContentType = metadata.ContentType;

        var destination = imageStorage.GetOriginalDestination(hash, metadata);
        if (!PathsEqual(original, destination))
        {
            MoveWithoutOverwrite(original, destination);
        }

        await db.SaveChangesAsync(cancellationToken);
        return $"Updated metadata to {metadata.ContentType} (.{metadata.Extension}).";
    }

    private string Relocate(StorageMaintenanceFinding finding)
    {
        var source = RequireSafeExistingPath(finding);
        var destination = finding.ExpectedPath;
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new InvalidOperationException("The finding has no deterministic destination.");
        }

        EnsureInsideFilesRoot(destination);
        MoveWithoutOverwrite(source, destination);
        return $"Moved file to {destination}.";
    }

    private string Quarantine(StorageMaintenanceFinding finding, Guid repairJobId)
    {
        var source = RequireSafeExistingPath(finding);
        var relative = fileSystem.Path.GetRelativePath(_filesRoot, source);
        var destination = fileSystem.Path.Join(_quarantineRoot, repairJobId.ToString("N"), relative);
        var directory = fileSystem.Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.Directory.CreateDirectory(directory);
        }

        destination = GetAvailablePath(destination);
        fileSystem.File.Move(source, destination);
        return $"Quarantined file at {destination}.";
    }

    private void MoveWithoutOverwrite(string source, string destination)
    {
        if (fileSystem.File.Exists(destination))
        {
            throw new IOException($"The repair destination already exists: {destination}");
        }

        var directory = fileSystem.Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.Directory.CreateDirectory(directory);
        }

        fileSystem.File.Move(source, destination);
    }

    private string GetAvailablePath(string path)
    {
        if (!fileSystem.File.Exists(path))
        {
            return path;
        }

        var directory = fileSystem.Path.GetDirectoryName(path)!;
        var name = fileSystem.Path.GetFileNameWithoutExtension(path);
        var extension = fileSystem.Path.GetExtension(path);
        return fileSystem.Path.Join(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    private string RequireSafeExistingPath(StorageMaintenanceFinding finding)
    {
        var path = FindExistingPath(finding)
            ?? throw new FileNotFoundException("The file recorded by the finding no longer exists.", finding.Path);
        EnsureInsideFilesRoot(path);
        return path;
    }

    private string? FindExistingPath(StorageMaintenanceFinding finding) =>
        !string.IsNullOrWhiteSpace(finding.Path) && fileSystem.File.Exists(finding.Path)
            ? fileSystem.Path.GetFullPath(finding.Path)
            : null;

    private void EnsureInsideFilesRoot(string path)
    {
        var fullPath = fileSystem.Path.GetFullPath(path);
        var rootPrefix = _filesRoot.TrimEnd(fileSystem.Path.DirectorySeparatorChar) + fileSystem.Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to repair a path outside the managed content store.");
        }
    }

    private bool PathsEqual(string left, string right) =>
        string.Equals(fileSystem.Path.GetFullPath(left), fileSystem.Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private async Task<HashItem> FindHashItem(ContentHash hash, CancellationToken cancellationToken) =>
        await db.Hashes.SingleOrDefaultAsync(h => h.Hash == hash.Bytes, cancellationToken)
        ?? throw new InvalidOperationException("The database record for the finding no longer exists.");

    private static ContentHash ParseHash(StorageMaintenanceFinding finding) =>
        string.IsNullOrWhiteSpace(finding.Hash)
            ? throw new InvalidOperationException("The finding has no content hash.")
            : ContentHash.FromHex(finding.Hash);
}
