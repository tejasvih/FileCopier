using System.Security.Cryptography;
using System.Text;

namespace FileCopier;

internal sealed record SourceFile(
    string ObjectId,
    string PersistentId,
    string PhonePath,
    string RelativePath,
    string DestinationPath,
    ulong Length,
    DateTime? CreationTime,
    DateTime? LastWriteTime,
    Action<Stream> Download);

internal static class BackupPlan
{
    public static IReadOnlySet<string> ResolveExclusions(
        IReadOnlyList<SourceFile> files, IEnumerable<string>? excludedPaths)
    {
        var exclusions = new HashSet<string>(excludedPaths?.Select(path => path.Replace('/', '\\')) ?? [],
            StringComparer.OrdinalIgnoreCase);
        var sourcePaths = files.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = exclusions.Where(path => !sourcePaths.Contains(path)).ToList();
        if (missing.Count != 0)
            throw new ArgumentException($"Excluded file paths were not found in the source: {string.Join(", ", missing)}. No files were copied.");
        return exclusions;
    }

    public static List<SourceFile> Create(
        string destinationRoot,
        IEnumerable<SourceFile> files,
        Func<string, bool>? fileExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(files);
        fileExists ??= File.Exists;

        var uniqueFiles = new List<SourceFile>();
        var objects = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(file.Download);
            if (string.IsNullOrWhiteSpace(file.ObjectId))
                throw new IOException($"Source file '{file.PhonePath}' has no native object ID.");

            // The plan owns destination paths; never trust a precomputed path outside the root.
            var normalized = file with
            {
                DestinationPath = BackupFile.GetDestinationPath(destinationRoot, file.RelativePath)
            };
            if (objects.TryGetValue(file.ObjectId, out var previous))
            {
                if (!HasConsistentMetadata(previous, normalized))
                    throw new IOException($"Native object '{file.ObjectId}' was returned with contradictory metadata.");

                continue;
            }

            objects.Add(file.ObjectId, normalized);
            uniqueFiles.Add(normalized);
        }

        var plan = new List<SourceFile>(uniqueFiles.Count);
        foreach (var group in uniqueFiles.GroupBy(file => file.DestinationPath, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                var persistentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var file in group)
                {
                    if (string.IsNullOrWhiteSpace(file.PersistentId) || !persistentIds.Add(file.PersistentId))
                    {
                        throw new IOException($"Distinct source objects named '{file.RelativePath}' require distinct, nonempty persistent IDs.");
                    }
                }

                // Every colliding object gets its own stable name. The ambiguous original stays untouched.
                foreach (var file in group)
                {
                    var persistentDestination = GetPersistentDestination(destinationRoot, file)
                        ?? throw new IOException($"Source filename '{file.RelativePath}' has an extension too long for a stable backup name.");
                    plan.Add(file with { DestinationPath = persistentDestination });
                }
            }
            else
            {
                var file = group.First();
                if (!string.IsNullOrWhiteSpace(file.PersistentId))
                {
                    var persistentDestination = GetPersistentDestination(destinationRoot, file);
                    if (persistentDestination != null && fileExists(persistentDestination))
                        file = file with { DestinationPath = persistentDestination };
                }
                plan.Add(file);
            }
        }

        var collision = plan.GroupBy(file => file.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision != null)
            throw new IOException($"Distinct source objects still map to '{collision.Key}'. No files were copied.");

        return plan;
    }

    private static bool HasConsistentMetadata(SourceFile left, SourceFile right) =>
        left.PersistentId == right.PersistentId &&
        left.PhonePath == right.PhonePath &&
        left.RelativePath == right.RelativePath &&
        left.DestinationPath.Equals(right.DestinationPath, StringComparison.OrdinalIgnoreCase) &&
        left.Length == right.Length &&
        left.CreationTime == right.CreationTime &&
        left.LastWriteTime == right.LastWriteTime;

    private static string? GetPersistentDestination(string destinationRoot, SourceFile file)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.PersistentId)))[..16]
            .ToLowerInvariant();
        var suffix = "__" + hash;
        var name = Path.GetFileName(file.RelativePath);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var availableStemLength = 255 - suffix.Length - extension.Length;
        if (availableStemLength < 0)
            return null;

        if (stem.Length > availableStemLength)
        {
            if (availableStemLength > 0 && char.IsHighSurrogate(stem[availableStemLength - 1]) &&
                char.IsLowSurrogate(stem[availableStemLength]))
            {
                availableStemLength--;
            }
            stem = stem[..availableStemLength];
        }

        var relativePath = Path.Combine(Path.GetDirectoryName(file.RelativePath) ?? "", stem + suffix + extension);
        return BackupFile.GetDestinationPath(destinationRoot, relativePath);
    }
}
