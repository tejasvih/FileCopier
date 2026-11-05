using System.Diagnostics;
using MediaDevices;

namespace FileCopier;

internal sealed class iPhoneConnect : IDisposable
{
    private readonly AppConfig config;
    private readonly Stopwatch stopwatch = new();
    private MediaDevice? device;

    public iPhoneConnect(AppConfig config)
    {
        this.config = config;
    }

    public string PhoneName => device?.FriendlyName ?? device?.Description ?? "iPhone";
    public int FolderCount { get; private set; }
    public int DiscoveredFolderCount { get; private set; }
    public int FoundFiles { get; private set; }
    public int CopiedFiles { get; private set; }
    public int SkippedFiles { get; private set; }
    public int ExcludedFiles { get; private set; }
    public ulong CopiedBytes { get; private set; }
    public List<(string Path, string Message)> Errors { get; } = [];

    public void Connect()
    {
        var devices = MediaDeviceManager.Instance.GetDevices()?.ToList() ?? [];
        var phones = devices.Where(IsApplePhone).ToList();
        if (phones.Count != 1)
        {
            var available = string.Join(", ", devices.Select(d => d.FriendlyName ?? d.Description ?? "Unnamed device"));
            throw new InvalidOperationException(phones.Count == 0
                ? $"No iPhone found. Available devices: {(available.Length == 0 ? "none" : available)}. Unlock iPhone and allow this computer to access photos."
                : "Multiple Apple devices found. Connect only the iPhone you want to copy.");
        }

        // Keep the selected device for cleanup even if opening the connection fails.
        device = phones[0];
        // Request a fresh file listing rather than the driver's cached folder view.
        device.Connect(access: MediaDeviceAccess.GenericRead, enableCache: false);
        // Do not call GetDrives(): some iPhone/driver versions fail when storage
        // properties are requested (0x80042008), although folder access still works.
    }

    private static bool IsApplePhone(MediaDevice candidate)
    {
        return candidate.FriendlyName?.Contains("iPhone", StringComparison.OrdinalIgnoreCase) == true
            || candidate.Description?.Contains("iPhone", StringComparison.OrdinalIgnoreCase) == true
            || candidate.Manufacturer?.Contains("Apple", StringComparison.OrdinalIgnoreCase) == true;
    }

    public void Diagnose()
    {
        var connectedDevice = RequireDevice();
        Console.WriteLine($"Phone: {PhoneName}");
        Console.WriteLine($"Source Path: {config.SourcePath}");
        Console.WriteLine("Read-only diagnosis; no photos will be downloaded.");
        Console.WriteLine("Folders visible at device root:");
        foreach (var directory in connectedDevice.GetRootDirectory().EnumerateDirectories("*"))
        {
            Console.WriteLine($" - {directory.FullName}");
        }

        var source = connectedDevice.GetDirectoryInfo(config.SourcePath);
        var folders = source.EnumerateDirectories("*").ToList();
        var files = source.EnumerateFiles("*").Count();
        Console.WriteLine($"Source accessible: {folders.Count} immediate folders, {files} immediate files.");
        foreach (var folder in folders)
        {
            Console.WriteLine($" - {folder.FullName}");
        }
    }

    public void CopyPhotos(CancellationToken cancellationToken, bool scanOnly = false,
        IReadOnlySet<string>? excludedFiles = null)
    {
        var connectedDevice = RequireDevice();
        FolderCount = FoundFiles = CopiedFiles = SkippedFiles = ExcludedFiles = 0;
        DiscoveredFolderCount = 1;
        CopiedBytes = 0;
        Errors.Clear();
        stopwatch.Restart();

        try
        {
            CheckCancellation(cancellationToken);
            ConsoleUtils.WriteLine("\nReading iPhone folders (Q or Ctrl+C stops after the current operation)...");
            // Resolve the configured path from the device root, without drive metadata.
            var source = connectedDevice.GetDirectoryInfo(config.SourcePath);
            var sourceFiles = BackupPlan.Create(config.DestinationPath, CollectFiles(source, cancellationToken));
            FoundFiles = sourceFiles.Count;
            if (FoundFiles == 0)
            {
                throw new IOException("No files found in the source. Check SourcePath, unlock iPhone, and allow photo access before retrying.");
            }

            try
            {
                config.ValidateSourceFileCount(FoundFiles);
            }
            catch (IOException ex)
            {
                RecordError(config.SourcePath, ex);
                throw;
            }

            if (scanOnly) return;

            // Validate exclusions against the complete plan before any transfer.
            // Match original phone paths, including when backup names have ID suffixes.
            var exclusions = BackupPlan.ResolveExclusions(sourceFiles, excludedFiles);

            ConsoleUtils.WriteLine($"\nFound {FoundFiles} files. Checking destination and copying changes...");
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
                        continue;
                    }

                    BackupFile.Copy(file.DestinationPath, file.Length, file.CreationTime, file.LastWriteTime,
                        file.Download);
                    CopiedFiles++;
                    CopiedBytes += file.Length;
                    var relativeDestination = Path.GetRelativePath(config.DestinationPath, file.DestinationPath);
                    ConsoleUtils.WriteLine($"{stopwatch.Elapsed:hh\\:mm\\:ss} Copied {CopiedFiles}, skipped {SkippedFiles}: {relativeDestination} ({file.Length.ToSize()})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RecordError(file.PhonePath, ex);
                    // A failed WPD read can leave later reads returning the previous
                    // object's bytes. Equal-sized files would pass length checks.
                    // End this connection instead of trusting any subsequent transfer.
                    ConsoleUtils.WriteWarning("Copying stopped at the first failed file. Reconnect the iPhone before retrying a failed transfer.");
                    throw;
                }
            }
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private List<SourceFile> CollectFiles(MediaDirectoryInfo source, CancellationToken cancellationToken)
    {
        var files = new List<SourceFile>();
        var root = new SourceFolder(source, "");
        // Read each native directory listing once. Queue child folders before
        // processing buffered file metadata so a file error cannot hide descendants.
        FolderScanner.Scan(root, ReadChildFolders, ReadFiles,
            (folder, exception) => RecordError(folder.Directory.FullName, exception),
            () => CheckCancellation(cancellationToken));
        if (FolderCount != DiscoveredFolderCount)
        {
            throw new IOException($"Incomplete source scan: visited {FolderCount} of {DiscoveredFolderCount} discovered folders.");
        }
        return files;

        IEnumerable<SourceFolder> ReadChildFolders(SourceFolder current)
        {
            FolderCount++;
            ConsoleUtils.WriteLine($"Scanning folder {FolderCount}/{DiscoveredFolderCount}: {current.Directory.FullName}...");
            foreach (var entry in current.Directory.EnumerateFileSystemInfos("*"))
            {
                CheckCancellation(cancellationToken);
                if (entry is MediaFileInfo file)
                {
                    current.Files.Add(file);
                    if (current.Files.Count % 100 == 0)
                    {
                        ConsoleUtils.WriteLine($"  {current.Directory.FullName}: read metadata for {current.Files.Count} files...");
                    }
                    continue;
                }

                if (entry is not MediaDirectoryInfo folder)
                {
                    throw new IOException($"Unsupported source entry: {entry.FullName}.");
                }

                var relativePath = Path.Combine(current.RelativePath, folder.Name);
                var valid = true;
                try
                {
                    BackupFile.GetDestinationPath(config.DestinationPath, relativePath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    valid = false;
                    RecordError(folder.FullName, ex);
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
                        files.Add(new SourceFile(file.Id, file.PersistentUniqueId, file.FullName, relativePath,
                            BackupFile.GetDestinationPath(config.DestinationPath, relativePath),
                            file.Length, file.CreationTime, file.LastWriteTime, file.CopyTo));
                        FoundFiles++;
                        folderFiles++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        RecordError(file.FullName, ex);
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

    private MediaDevice RequireDevice()
    {
        return device is { IsConnected: true }
            ? device
            : throw new InvalidOperationException("Connect to iPhone before accessing photos.");
    }

    public void Dispose()
    {
        if (device == null) return;
        try
        {
            if (device.IsConnected) device.Disconnect();
        }
        finally
        {
            device.Dispose();
            device = null;
        }
    }

    private sealed record SourceFolder(MediaDirectoryInfo Directory, string RelativePath)
    {
        public List<MediaFileInfo> Files { get; } = [];
    }
}
