namespace FileCopier;

internal static class BackupFile
{
    public static bool ShouldCopy(string destinationPath, ulong length, DateTime? lastWriteTime)
    {
        var destination = new FileInfo(destinationPath);
        return !destination.Exists ||
            destination.Length == 0 ||
            (ulong)destination.Length != length ||
            (lastWriteTime.HasValue && destination.LastWriteTime != lastWriteTime.Value);
    }

    public static string GetDestinationPath(string destinationRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Phone file path must be relative.", nameof(relativePath));

        var segments = relativePath.Split(['\\', '/']);
        foreach (var segment in segments)
        {
            if (segment.Length is 0 or > 255 || segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                throw new ArgumentException("Phone file path contains an invalid Windows filename.", nameof(relativePath));
            }

            var baseName = segment.Split('.')[0].TrimEnd(' ');
            if (IsReservedName(baseName))
                throw new ArgumentException("Phone file path contains a reserved Windows filename.", nameof(relativePath));
        }

        var root = Path.GetFullPath(destinationRoot);
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Phone file path escapes the destination folder.", nameof(relativePath));

        return destination;
    }

    public static void Copy(
        string destinationPath,
        ulong length,
        DateTime? creationTime,
        DateTime? lastWriteTime,
        Action<Stream> download)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(download);
        if (length == 0)
            throw new IOException("The iPhone reports zero bytes for this file; refusing to publish an empty backup.");

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".iphone-photo-{Guid.NewGuid():N}.partial");
        var ownsTemporaryFile = false;

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryFile = true;
                download(stream);
                if ((ulong)stream.Length != length)
                    throw new IOException($"Downloaded file has {stream.Length} bytes; expected {length} bytes.");

                stream.Flush(flushToDisk: true);
            }

            if (creationTime.HasValue)
                File.SetCreationTime(temporaryPath, creationTime.Value);
            if (lastWriteTime.HasValue)
                File.SetLastWriteTime(temporaryPath, lastWriteTime.Value);

            File.Move(temporaryPath, destination, overwrite: true);
            ownsTemporaryFile = false;
        }
        finally
        {
            if (ownsTemporaryFile)
            {
                // Preserve the transfer error if an interrupted download cannot be cleaned up.
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsReservedName(string name)
    {
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Length == 4 &&
            (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            (name[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }
}
