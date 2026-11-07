using System.Net.Sockets;
using System.Security.Authentication;

namespace FileCopier.Afc;

/// <summary>
/// Reads the iPhone camera roll through AFC. Each request is matched to its reply and a
/// failed session is discarded, so a retry or a later file can never receive another
/// file's bytes. Transient USB/session failures reconnect and retry the operation.
/// </summary>
/// <remarks>
/// Originals come from /DCIM/1xxAPPLE. Edited versions live in
/// /PhotoData/Mutations/DCIM/1xxAPPLE/IMG_n/Adjustments and are listed in the same folder
/// under the names Windows (WPD) gives them: FullSizeRender.ext as IMG_En.EXT and
/// Adjustments.plist as IMG_n.AAE; a camera-roll IMG_n.AAE of an edited item becomes
/// IMG_On.AAE. Other files there (Penultimate*, previews) are the Photos app's working
/// files, which Windows does not export either. Photos synced onto the phone from a
/// computer (iTunes/Finder sync) live in /PhotoData/Sync/100SYNCD and keep that folder name.
/// </remarks>
internal sealed class AfcSource : IPhoneSource
{
    public const string CameraRollPath = "/DCIM";
    public const string EditsPath = "/PhotoData/Mutations/DCIM";
    public const string SyncedPath = "/PhotoData/Sync";
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    private UsbmuxDevice? device;
    private Lockdown? lockdown;
    private AfcClient? afc;
    private string phoneName = "iPhone";
    private string productVersion = "";

    public string PhoneName => phoneName;
    public string SourcePath => CameraRollPath;
    public bool IsolatesFailures => true;

    public void Connect()
    {
        var devices = Usbmux.ListDevices().Where(d => d.ConnectionType == "USB").ToList();
        if (devices.Count == 0)
            throw new IOException("No iPhone found through Apple Mobile Device Service. Connect it by USB, unlock it, and tap Trust.");
        if (devices.Count > 1)
            throw new InvalidOperationException("Multiple Apple devices found. Connect only the iPhone you want to copy.");
        device = devices[0];
        Open();
    }

    public void Diagnose()
    {
        Console.WriteLine($"Phone: {PhoneName} (iOS {productVersion}, UDID {device?.Udid})");
        Console.WriteLine("Transport: AFC through Apple Mobile Device Service.");
        Console.WriteLine("Read-only diagnosis; no photos will be downloaded.");
        Console.WriteLine("Folders visible at media root:");
        foreach (var name in Run(client => client.ReadDirectory("/")))
            Console.WriteLine($" - /{name}");

        var entries = Run(client => client.ReadDirectory(CameraRollPath));
        Console.WriteLine($"{CameraRollPath} accessible: {entries.Count} immediate entries.");
        foreach (var name in entries)
        {
            var path = $"{CameraRollPath}/{name}";
            var info = Run(client => client.GetFileInfo(path));
            var detail = info.IsDirectory ? $"{Run(client => client.ReadDirectory(path)).Count} entries" : info.Size.ToSize();
            Console.WriteLine($" - {path} ({detail})");
        }

        var editFolders = ListDirectories(EditsPath);
        var editedItems = editFolders.Sum(name => Run(client => client.ReadDirectory($"{EditsPath}/{name}")).Count);
        Console.WriteLine($"{EditsPath} accessible: {editedItems} edited items in {editFolders.Count} folders.");

        var syncedFolders = ListDirectories(SyncedPath);
        var syncedFiles = syncedFolders.Sum(name => Run(client => client.ReadDirectory($"{SyncedPath}/{name}")).Count);
        Console.WriteLine($"{SyncedPath} accessible: {syncedFiles} photos synced from a computer in {syncedFolders.Count} folders.");
    }

    public PhoneFolderEntry OpenRoot() => new("", CameraRollPath, EnumerateRoot);

    private IEnumerable<PhoneEntry> EnumerateRoot()
    {
        var editFolders = ListDirectories(EditsPath).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Enumerate(CameraRollPath))
        {
            editFolders.Remove(entry.Name);
            yield return entry is PhoneFolderEntry folder ? CameraFolder(folder.Name) : entry;
        }
        // Edits whose original folder is gone are still backed up.
        foreach (var name in editFolders.Order(StringComparer.Ordinal))
            yield return CameraFolder(name);

        // Library items synced from a computer are not in the camera roll.
        foreach (var name in ListDirectories(SyncedPath))
        {
            var path = $"{SyncedPath}/{name}";
            yield return new PhoneFolderEntry(name, path, () => Enumerate(path));
        }
    }

    private PhoneFolderEntry CameraFolder(string name) =>
        new(name, $"{CameraRollPath}/{name}", () => EnumerateCameraFolder(name));

    private IEnumerable<PhoneEntry> EnumerateCameraFolder(string name)
    {
        // Edits first: an item's current adjustments decide how Windows names its camera-roll .AAE.
        var edits = EnumerateEdits($"{EditsPath}/{name}").ToList();
        var editedAdjustments = edits.Select(edit => edit.Name)
            .Where(editName => editName.EndsWith(".AAE", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var originals = $"{CameraRollPath}/{name}";
        if (Exists(originals))
        {
            foreach (var entry in Enumerate(originals))
            {
                yield return entry is PhoneFileEntry file && editedAdjustments.Contains(file.Name)
                    ? file with { Name = GetOriginalAdjustmentsName(file.Name) }
                    : entry;
            }
        }
        foreach (var edit in edits)
            yield return edit;
    }

    private IEnumerable<PhoneEntry> Enumerate(string path)
    {
        foreach (var name in Run(client => client.ReadDirectory(path)))
        {
            var childPath = $"{path}/{name}";
            var info = Run(client => client.GetFileInfo(childPath));
            if (info.IsDirectory)
                yield return new PhoneFolderEntry(name, childPath, () => Enumerate(childPath));
            else if (info.IsFile)
                yield return FileEntry(name, childPath, info);
            else
                throw new IOException($"Unsupported source entry: {childPath}.");
        }
    }

    private IEnumerable<PhoneFileEntry> EnumerateEdits(string path)
    {
        if (!Exists(path)) yield break;
        foreach (var item in Run(client => client.ReadDirectory(path)))
        {
            var itemPath = $"{path}/{item}";
            if (!Run(client => client.GetFileInfo(itemPath)).IsDirectory ||
                !Run(client => client.ReadDirectory(itemPath)).Contains("Adjustments"))
            {
                continue;
            }

            var adjustments = $"{itemPath}/Adjustments";
            foreach (var name in Run(client => client.ReadDirectory(adjustments)))
            {
                var backupName = GetEditBackupName(item, name);
                if (backupName == null) continue;
                var filePath = $"{adjustments}/{name}";
                var info = Run(client => client.GetFileInfo(filePath));
                if (info.IsFile) yield return FileEntry(backupName, filePath, info);
            }
        }
    }

    /// <summary>Maps an edit file to the name Windows exports it under, or null when Windows does not export it.</summary>
    internal static string? GetEditBackupName(string item, string fileName)
    {
        if (fileName == "Adjustments.plist") return $"{item}.AAE";

        const string render = "FullSizeRender.";
        if (!fileName.StartsWith(render, StringComparison.Ordinal) || fileName.Length == render.Length) return null;
        return $"{Variant(item, 'E')}.{fileName[render.Length..].ToUpperInvariant()}";
    }

    /// <summary>
    /// Older iOS versions kept capture adjustments as IMG_n.AAE in the camera roll. Once the item
    /// is edited, Windows exports the current adjustments as IMG_n.AAE and that file as IMG_On.AAE.
    /// </summary>
    internal static string GetOriginalAdjustmentsName(string fileName) =>
        $"{Variant(Path.GetFileNameWithoutExtension(fileName), 'O')}{Path.GetExtension(fileName)}";

    // IMG_8020 -> IMG_E8020 / IMG_O8020, as Windows names edited items and their original adjustments.
    private static string Variant(string item, char marker) =>
        item.StartsWith("IMG_", StringComparison.Ordinal) ? $"IMG_{marker}{item[4..]}" : $"{item}_{marker}";

    // The AFC path is the object's identity: unique on the phone and stable across sessions.
    private PhoneFileEntry FileEntry(string name, string path, AfcFileInfo info) =>
        new(name, path, path, path, info.Size, info.CreationTime, info.LastWriteTime,
            destination => CopyFile(path, destination));

    private List<string> ListDirectories(string path) =>
        Exists(path)
            ? Run(client => client.ReadDirectory(path))
                .Where(name => Run(client => client.GetFileInfo($"{path}/{name}")).IsDirectory)
                .ToList()
            : [];

    private bool Exists(string path)
    {
        try
        {
            return Run(client => client.GetFileInfo(path)).IsDirectory;
        }
        catch (AfcException ex) when (ex.Status == AfcException.ObjectNotFound)
        {
            return false;
        }
    }

    private void CopyFile(string path, Stream destination)
    {
        // A retried download restarts the destination from where this file began.
        var start = destination.CanSeek ? destination.Position : -1;
        Run(client =>
        {
            if (start >= 0)
            {
                destination.SetLength(start);
                destination.Position = start;
            }
            return client.CopyFile(path, destination);
        }, retry: start >= 0);
    }

    private T Run<T>(Func<AfcClient, T> operation, bool retry = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return operation(afc ?? Open());
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                // Never reuse a session after a transient failure.
                Close();
                if (!retry || attempt >= RetryDelays.Length) throw;
                ConsoleUtils.WriteWarning($"iPhone read failed ({ex.Message}). Reconnecting in {RetryDelays[attempt].TotalSeconds:0} s, retry {attempt + 1} of {RetryDelays.Length}...");
                Thread.Sleep(RetryDelays[attempt]);
            }
        }
    }

    // Missing files, folders and permission errors are deterministic; retrying cannot help.
    private static bool IsTransient(Exception ex) => ex switch
    {
        AfcException afcError => !afcError.IsPermanent,
        IOException or InvalidDataException or SocketException or AuthenticationException => true,
        _ => false,
    };

    private AfcClient Open()
    {
        if (device == null) throw new InvalidOperationException("Connect to iPhone before accessing photos.");
        Close();
        lockdown = Lockdown.Start(device);
        phoneName = lockdown.DeviceName;
        productVersion = lockdown.ProductVersion;
        afc = new AfcClient(lockdown.StartService("com.apple.afc"));
        return afc;
    }

    private void Close()
    {
        try { afc?.Dispose(); }
        finally
        {
            afc = null;
            try { lockdown?.Dispose(); }
            finally { lockdown = null; }
        }
    }

    public void Dispose() => Close();
}
