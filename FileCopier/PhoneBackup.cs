using System.Diagnostics;

namespace FileCopier;

/// <summary>Scans an iPhone source and copies new or changed files to the destination.</summary>
internal sealed class PhoneBackup(AppConfig config, IPhoneSource source, string destinationPath)
{
    // With failure-isolating sources, a run continues past a failed file but stops when the
    // phone keeps failing (for example, unplugged or locked), instead of failing every file.
    internal const int MaximumConsecutiveFailures = 3;

    private readonly Stopwatch stopwatch = new();

    public int FolderCount { get; private set; }
    public int DiscoveredFolderCount { get; private set; }
    public int FoundFiles { get; private set; }
    public int CopiedFiles { get; private set; }
    public int SkippedFiles { get; private set; }
    public int ExcludedFiles { get; private set; }
    public ulong CopiedBytes { get; private set; }
    public List<(string Path, string Message)> Errors { get; } = [];

    public void CopyPhotos(CancellationToken cancellationToken, bool scanOnly = false,
        IReadOnlySet<string>? excludedFiles = null)
    {
        FolderCount = FoundFiles = CopiedFiles = SkippedFiles = ExcludedFiles = 0;
        DiscoveredFolderCount = 1;
        CopiedBytes = 0;
        Errors.Clear();
        stopwatch.Restart();

        try
        {
            CheckCancellation(cancellationToken);
            ConsoleUtils.WriteLine("\nReading iPhone folders (Q or Ctrl+C stops after the current operation)...");
            var sourceFiles = BackupPlan.Create(destinationPath, CollectFiles(source.OpenRoot(), cancellationToken));
            FoundFiles = sourceFiles.Count;
            if (FoundFiles == 0)
            {
                throw new IOException("No files found in the source. Check SourcePath, unlock iPhone, and allow photo access before retrying.");
            }

            // Both transports list the same library (originals, .AAE sidecars, edited renders),
            // so one expected count guards either against a reduced device view.
            try
            {
                config.ValidateSourceFileCount(FoundFiles);
            }
            catch (IOException ex)
            {
                RecordError(source.SourcePath, ex);
                throw;
            }

            if (scanOnly)
            {
                PreviewCopy(sourceFiles);
                return;
            }

            // Validate exclusions against the complete plan before any transfer.
            // Match original phone paths, including when backup names have ID suffixes.
            var exclusions = BackupPlan.ResolveExclusions(sourceFiles, excludedFiles);

            ConsoleUtils.WriteLine($"\nFound {FoundFiles} files. Checking destination and copying changes...");
            var consecutiveFailures = 0;
            foreach (var file in sourceFiles)
            {
                CheckCancellation(cancellationToken);
                if (exclusions.Contains(file.RelativePath))
                {
                    ExcludedFiles++;
                    RecordError(file.PhonePath, new IOException("Excluded by request; this source file was not copied or verified."));
                    continue;
                }
                try
                {
                    if (!BackupFile.ShouldCopy(file.DestinationPath, file.Length, file.LastWriteTime))
                    {
                        SkippedFiles++;
                        consecutiveFailures = 0;
                        continue;
                    }

                    BackupFile.Copy(file.DestinationPath, file.Length, file.CreationTime, file.LastWriteTime,
                        file.Download);
                    CopiedFiles++;
                    CopiedBytes += file.Length;
                    consecutiveFailures = 0;
                    var relativeDestination = Path.GetRelativePath(destinationPath, file.DestinationPath);
                    ConsoleUtils.WriteLine($"{stopwatch.Elapsed:hh\\:mm\\:ss} Copied {CopiedFiles}, skipped {SkippedFiles}: {relativeDestination} ({file.Length.ToSize()})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RecordError(file.PhonePath, ex);
                    if (source.IsolatesFailures && ++consecutiveFailures < MaximumConsecutiveFailures)
                        continue;

                    ConsoleUtils.WriteWarning(source.IsolatesFailures
                        ? $"Copying stopped after {MaximumConsecutiveFailures} consecutive failed files. Reconnect and unlock the iPhone, then rerun to continue."
                        : "Copying stopped at the first failed file. Reconnect the iPhone before retrying a failed transfer.");
                    throw;
                }
            }
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    // Read-only: compares the plan with the destination without reading file contents.
    private void PreviewCopy(IReadOnlyList<SourceFile> sourceFiles)
    {
        var toCopy = sourceFiles.Where(file => BackupFile.ShouldCopy(file.DestinationPath, file.Length, file.LastWriteTime)).ToList();
        var bytesToCopy = toCopy.Aggregate(0UL, (total, file) => total + file.Length);
        ConsoleUtils.WriteLine($"\nCopy preview for {destinationPath}: {sourceFiles.Count - toCopy.Count} up to date; {toCopy.Count} to copy ({bytesToCopy.ToSize()}).");
    }

    private List<SourceFile> CollectFiles(PhoneFolderEntry rootEntry, CancellationToken cancellationToken)
    {
        var files = new List<SourceFile>();
        var root = new SourceFolder(rootEntry, "");
        // Read each native directory listing once. Queue child folders before
        // processing buffered file metadata so a file error cannot hide descendants.
        FolderScanner.Scan(root, ReadChildFolders, ReadFiles,
            (folder, exception) => RecordError(folder.Entry.PhonePath, exception),
            () => CheckCancellation(cancellationToken));
        if (FolderCount != DiscoveredFolderCount)
        {
            throw new IOException($"Incomplete source scan: visited {FolderCount} of {DiscoveredFolderCount} discovered folders.");
        }
        return files;

        IEnumerable<SourceFolder> ReadChildFolders(SourceFolder current)
        {
            FolderCount++;
            ConsoleUtils.WriteLine($"Scanning folder {FolderCount}/{DiscoveredFolderCount}: {current.Entry.PhonePath}...");
            foreach (var entry in current.Entry.Enumerate())
            {
                CheckCancellation(cancellationToken);
                if (entry is PhoneFileEntry file)
                {
                    current.Files.Add(file);
                    if (current.Files.Count % 100 == 0)
                    {
                        ConsoleUtils.WriteLine($"  {current.Entry.PhonePath}: read metadata for {current.Files.Count} files...");
                    }
                    continue;
                }

                if (entry is not PhoneFolderEntry folder)
                {
                    throw new IOException($"Unsupported source entry: {entry.PhonePath}.");
                }

                var relativePath = Path.Combine(current.RelativePath, folder.Name);
                var valid = true;
                try
                {
                    BackupFile.GetDestinationPath(destinationPath, relativePath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    valid = false;
                    RecordError(folder.PhonePath, ex);
                }
                if (valid)
                {
                    DiscoveredFolderCount++;
                    yield return new SourceFolder(folder, relativePath);
                }
            }
            if (current.RelativePath.Length == 0)
            {
                ConsoleUtils.WriteLine($"Discovered {DiscoveredFolderCount - 1} source subfolders; all are queued for scanning.");
            }
        }

        void ReadFiles(SourceFolder current)
        {
            var folderFiles = 0;
            try
            {
                foreach (var file in current.Files)
                {
                    CheckCancellation(cancellationToken);
                    try
                    {
                        var relativePath = Path.Combine(current.RelativePath, file.Name);
                        files.Add(new SourceFile(file.ObjectId, file.PersistentId, file.PhonePath, relativePath,
                            BackupFile.GetDestinationPath(destinationPath, relativePath),
                            file.Length, file.CreationTime, file.LastWriteTime, file.Download));
                        FoundFiles++;
                        folderFiles++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        RecordError(file.PhonePath, ex);
                    }
                }
            }
            finally
            {
                current.Files.Clear();
            }
            ConsoleUtils.WriteLine($"  Read {folderFiles} files. Folders visited: {FolderCount}/{DiscoveredFolderCount}.");
        }
    }

    private void RecordError(string path, Exception exception)
    {
        var message = $"{exception.Message} (0x{exception.HResult:X8})";
        Errors.Add((path, message));
        ConsoleUtils.WriteError($"{path}: {message}");
    }

    private static void CheckCancellation(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ConsoleUtils.IsQuitRequested())
        {
            throw new OperationCanceledException("Stopped by user.");
        }
    }

    public void PrintSummary()
    {
        Console.WriteLine($"\nFolders visited: {FolderCount}/{DiscoveredFolderCount} (including source root); files found: {FoundFiles}; copied: {CopiedFiles} ({CopiedBytes.ToSize()}); skipped: {SkippedFiles}; excluded: {ExcludedFiles}; errors: {Errors.Count}.");
    }

    private sealed record SourceFolder(PhoneFolderEntry Entry, string RelativePath)
    {
        public List<PhoneFileEntry> Files { get; } = [];
    }
}
