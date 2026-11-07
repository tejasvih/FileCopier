using System.Diagnostics;

namespace FileCopier;

/// <summary>
/// Scans an iPhone source, then copies new or changed files to the destination or verifies the
/// backup against the phone.
/// </summary>
internal sealed class PhoneBackup(IPhoneSource source, string destinationPath)
{
    // With failure-isolating sources, a run continues past a failed file but stops when the
    // phone keeps failing (for example, unplugged or locked), instead of failing every file.
    internal const int MaximumConsecutiveFailures = 3;

    private readonly Stopwatch stopwatch = new();
    private bool verifying;
    private int[] folderNumbers = [];
    private int folderCount;

    public int FolderCount { get; private set; }
    public int DiscoveredFolderCount { get; private set; }
    public int FoundFiles { get; private set; }
    public int CopiedFiles { get; private set; }
    public int SkippedFiles { get; private set; }
    public int ExcludedFiles { get; private set; }
    public int EmptyFiles { get; private set; }
    public int VerifiedFiles { get; private set; }
    public ulong CopiedBytes { get; private set; }
    public ulong VerifiedBytes { get; private set; }
    public List<(string Path, string Message)> Errors { get; } = [];

    public void CopyPhotos(CancellationToken cancellationToken, bool scanOnly = false,
        IReadOnlySet<string>? excludedFiles = null)
    {
        Run(cancellationToken, verify: false, sourceFiles =>
        {
            if (scanOnly)
            {
                PreviewCopy(sourceFiles);
                return;
            }

            // Validate exclusions against the complete plan before any transfer.
            // Match original phone paths, including when backup names have ID suffixes.
            var exclusions = BackupPlan.ResolveExclusions(sourceFiles, excludedFiles);

            ConsoleUtils.WriteHeading($"\nFound {FoundFiles} files in {folderCount} folders. Checking destination and copying changes...");
            var consecutiveFailures = 0;
            for (var index = 0; index < sourceFiles.Count; index++)
            {
                var file = sourceFiles[index];
                CheckCancellation(cancellationToken);
                if (exclusions.Contains(file.RelativePath))
                {
                    ExcludedFiles++;
                    RecordError(file.PhonePath, new IOException("Excluded by request; this source file was not copied or verified."));
                    continue;
                }
                if (SkipEmpty(file)) continue;
                try
                {
                    if (!BackupFile.ShouldCopy(file.DestinationPath, file.Length, file.LastWriteTime))
                    {
                        SkippedFiles++;
                        consecutiveFailures = 0;
                        ConsoleUtils.WriteSkipped($"{Progress(index)}. Skipped {SkippedFiles}, same size and date in backup: {RelativeDestination(file)}");
                        continue;
                    }

                    BackupFile.Copy(file.DestinationPath, file.Length, file.CreationTime, file.LastWriteTime,
                        file.Download);
                    CopiedFiles++;
                    CopiedBytes += file.Length;
                    consecutiveFailures = 0;
                    ConsoleUtils.WriteDone($"{Progress(index)}. Copied {CopiedFiles}, skipped {SkippedFiles}: {RelativeDestination(file)} ({file.Length.ToSize()})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (StopAfterFailure(file, ex, ref consecutiveFailures)) throw;
                }
            }
        });
    }

    /// <summary>
    /// Reads every file from the phone again and compares it byte for byte with its backup copy.
    /// Writes nothing to the phone or the backup.
    /// </summary>
    public void VerifyBackup(CancellationToken cancellationToken)
    {
        Run(cancellationToken, verify: true, sourceFiles =>
        {
            ConsoleUtils.WriteHeading($"\nFound {FoundFiles} files in {folderCount} folders. Reading each one from the iPhone and comparing it with the backup...");
            var consecutiveFailures = 0;
            for (var index = 0; index < sourceFiles.Count; index++)
            {
                var file = sourceFiles[index];
                CheckCancellation(cancellationToken);
                if (SkipEmpty(file)) continue;
                try
                {
                    var difference = BackupFile.Verify(file.DestinationPath, file.Length, file.Download);
                    consecutiveFailures = 0;
                    if (difference != null)
                    {
                        RecordError(file.PhonePath, difference);
                        continue;
                    }

                    VerifiedFiles++;
                    VerifiedBytes += file.Length;
                    ConsoleUtils.WriteDone($"{Progress(index)}. Verified {VerifiedFiles}: {RelativeDestination(file)} ({file.Length.ToSize()})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (StopAfterFailure(file, ex, ref consecutiveFailures)) throw;
                }
            }
        });
    }

    private void Run(CancellationToken cancellationToken, bool verify, Action<IReadOnlyList<SourceFile>> action)
    {
        FolderCount = FoundFiles = CopiedFiles = SkippedFiles = ExcludedFiles = EmptyFiles = VerifiedFiles = 0;
        DiscoveredFolderCount = 1;
        CopiedBytes = VerifiedBytes = 0;
        Errors.Clear();
        verifying = verify;
        stopwatch.Restart();

        try
        {
            CheckCancellation(cancellationToken);
            ConsoleUtils.WriteHeading("\nReading iPhone folders (Q or Ctrl+C stops after the current file; Ctrl+C twice stops at once)...");
            var sourceFiles = BackupPlan.Create(destinationPath, CollectFiles(source.OpenRoot(), cancellationToken));
            FoundFiles = sourceFiles.Count;
            if (FoundFiles == 0)
            {
                throw new IOException("No files found in the source. Check SourcePath, unlock iPhone, and allow photo access before retrying.");
            }

            NumberFolders(sourceFiles);
            action(sourceFiles);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    // Numbers each planned file's backup folder in the order the run reaches it.
    private void NumberFolders(IReadOnlyList<SourceFile> files)
    {
        var numbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        folderNumbers = new int[files.Count];
        for (var index = 0; index < files.Count; index++)
        {
            var folder = Path.GetDirectoryName(files[index].RelativePath) ?? "";
            if (!numbers.TryGetValue(folder, out var number))
                numbers.Add(folder, number = numbers.Count + 1);
            folderNumbers[index] = number;
        }
        folderCount = numbers.Count;
    }

    /// <summary>For example "00:12:03 File 1234/64831, folder 12/44".</summary>
    internal string Progress(int index) =>
        $"{stopwatch.Elapsed:hh\\:mm\\:ss} File {index + 1}/{FoundFiles}, folder {folderNumbers[index]}/{folderCount}";

    // Read-only: compares the plan with the destination without reading file contents.
    private void PreviewCopy(IReadOnlyList<SourceFile> sourceFiles)
    {
        EmptyFiles = sourceFiles.Count(IsEmptyOnPhone);
        var toCopy = sourceFiles.Where(file => !IsEmptyOnPhone(file) &&
            BackupFile.ShouldCopy(file.DestinationPath, file.Length, file.LastWriteTime)).ToList();
        var bytesToCopy = toCopy.Aggregate(0UL, (total, file) => total + file.Length);
        var emptyNote = EmptyFiles > 0 ? $"; {EmptyFiles} empty on the iPhone (nothing to copy)" : "";
        ConsoleUtils.WriteHeading($"\nCopy preview for {destinationPath}: {sourceFiles.Count - toCopy.Count - EmptyFiles} up to date; {toCopy.Count} to copy ({bytesToCopy.ToSize()}){emptyNote}.");
    }

    // An empty file the phone's file system reports has no content to lose. Sources whose
    // sizes are not exact keep zero as an error (see BackupFile.Copy).
    private bool IsEmptyOnPhone(SourceFile file) => file.Length == 0 && source.ReportsExactSizes;

    private bool SkipEmpty(SourceFile file)
    {
        if (!IsEmptyOnPhone(file)) return false;
        EmptyFiles++;
        ConsoleUtils.WriteWarning($"{file.PhonePath}: empty on the iPhone (0 bytes); nothing to {(verifying ? "verify" : "copy")}.");
        return true;
    }

    /// <summary>Records a failed phone read and returns true when the run must stop.</summary>
    private bool StopAfterFailure(SourceFile file, Exception exception, ref int consecutiveFailures)
    {
        RecordError(file.PhonePath, exception);
        if (source.IsolatesFailures && ++consecutiveFailures < MaximumConsecutiveFailures)
            return false;

        var stopped = verifying ? "Verification stopped" : "Copying stopped";
        ConsoleUtils.WriteWarning(source.IsolatesFailures
            ? $"{stopped} after {MaximumConsecutiveFailures} consecutive failed files. Reconnect and unlock the iPhone, then rerun."
            : $"{stopped} at the first failed file. Reconnect the iPhone before retrying.");
        return true;
    }

    private string RelativeDestination(SourceFile file) => Path.GetRelativePath(destinationPath, file.DestinationPath);

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
            ConsoleUtils.WriteStep($"Scanning folder {FolderCount}/{DiscoveredFolderCount}: {current.Entry.PhonePath}...");
            foreach (var entry in current.Entry.Enumerate())
            {
                CheckCancellation(cancellationToken);
                if (entry is PhoneFileEntry file)
                {
                    current.Files.Add(file);
                    if (current.Files.Count % 100 == 0)
                    {
                        ConsoleUtils.WriteDetail($"  {current.Entry.PhonePath}: read metadata for {current.Files.Count} files...");
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
                ConsoleUtils.WriteHeading($"Discovered {DiscoveredFolderCount - 1} source subfolders; all are queued for scanning.");
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
            ConsoleUtils.WriteDetail($"  Read {folderFiles} files. Folders visited: {FolderCount}/{DiscoveredFolderCount}.");
        }
    }

    private void RecordError(string path, Exception exception) =>
        RecordError(path, $"{exception.Message} (0x{exception.HResult:X8})");

    private void RecordError(string path, string message)
    {
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
        var work = verifying
            ? $"verified identical: {VerifiedFiles} ({VerifiedBytes.ToSize()})"
            : $"copied: {CopiedFiles} ({CopiedBytes.ToSize()}); skipped: {SkippedFiles}; excluded: {ExcludedFiles}";
        ConsoleUtils.WriteHeading($"\nBackup folder: {destinationPath}");
        Console.WriteLine($"Folders visited: {FolderCount}/{DiscoveredFolderCount} (including source root); files found: {FoundFiles}; {work}; empty on phone: {EmptyFiles}; errors: {Errors.Count}.");
    }

    private sealed record SourceFolder(PhoneFolderEntry Entry, string RelativePath)
    {
        public List<PhoneFileEntry> Files { get; } = [];
    }
}
