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

    /// <summary>
    /// Reads the phone file and compares it byte for byte with the backup copy, writing nothing.
    /// Returns null when they match, otherwise why the backup copy does not.
    /// </summary>
    public static string? Verify(string destinationPath, ulong length, Action<Stream> download)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(download);
        if (length == 0)
            throw new IOException("The iPhone reports zero bytes for this file; it cannot be verified.");

        var backup = new FileInfo(destinationPath);
        if (!backup.Exists)
            return "Missing from the backup. Copy again to add it.";
        if ((ulong)backup.Length != length)
            return $"Backup copy has {backup.Length} bytes; the iPhone has {length}. Copy again to replace it.";

        using var comparison = new BackupComparison(backup.FullName);
        download(comparison);
        if ((ulong)comparison.Length != length)
            throw new IOException($"iPhone sent {comparison.Length} bytes; expected {length} bytes.");

        // Same size and date means copying again would skip it, so the copy must be removed first.
        return comparison.FirstDifference is { } offset
            ? $"Backup copy differs from the iPhone at byte {offset}. Delete the backup copy, then copy again."
            : null;
    }

    /// <summary>
    /// A write-only stream that compares what the phone sends with the backup file. It never throws
    /// on a difference, so a mismatch is not mistaken for a transfer failure. A retried download
    /// rewinds it with SetLength and Position, like a destination file.
    /// </summary>
    private sealed class BackupComparison(string backupPath) : Stream
    {
        private readonly FileStream backup = new(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, FileOptions.SequentialScan);
        private byte[] expected = [];
        private long compared;

        public long? FirstDifference { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => compared;

        public override long Position
        {
            get => compared;
            set
            {
                if (value != compared)
                    throw new NotSupportedException("A backup comparison can only be rewound with SetLength.");
            }
        }

        public override void SetLength(long value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value > compared)
                throw new NotSupportedException("A backup comparison cannot be extended.");
            compared = value;
            if (FirstDifference >= value) FirstDifference = null;
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (expected.Length < buffer.Length) expected = new byte[buffer.Length];
            backup.Position = compared;
            var read = backup.ReadAtLeast(expected.AsSpan(0, buffer.Length), buffer.Length, throwOnEndOfStream: false);
            if (FirstDifference == null)
            {
                var common = Math.Min(read, buffer.Length);
                var index = buffer[..common].CommonPrefixLength(expected.AsSpan(0, common));
                if (index < buffer.Length) FirstDifference = compared + index;
            }
            compared += buffer.Length;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) backup.Dispose();
            base.Dispose(disposing);
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
