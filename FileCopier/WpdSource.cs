using MediaDevices;

namespace FileCopier;

/// <summary>Reads the iPhone through Windows Portable Devices (the Explorer "Apple iPhone" view).</summary>
internal sealed class WpdSource(string sourcePath) : IPhoneSource
{
    private MediaDevice? device;

    public string PhoneName => device?.FriendlyName ?? device?.Description ?? "iPhone";
    public string SourcePath => sourcePath;

    // A failed WPD read can leave later reads returning the previous object's bytes.
    // Equal-sized files would pass length checks, so a failure must end the run.
    public bool IsolatesFailures => false;

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
        Console.WriteLine($"Source Path: {sourcePath}");
        Console.WriteLine("Transport: WPD (Windows Portable Devices).");
        Console.WriteLine("Read-only diagnosis; no photos will be downloaded.");
        Console.WriteLine("Folders visible at device root:");
        foreach (var directory in connectedDevice.GetRootDirectory().EnumerateDirectories("*"))
        {
            Console.WriteLine($" - {directory.FullName}");
        }

        var source = connectedDevice.GetDirectoryInfo(sourcePath);
        var folders = source.EnumerateDirectories("*").ToList();
        var files = source.EnumerateFiles("*").Count();
        Console.WriteLine($"Source accessible: {folders.Count} immediate folders, {files} immediate files.");
        foreach (var folder in folders)
        {
            Console.WriteLine($" - {folder.FullName}");
        }
    }

    // Resolve the configured path from the device root, without drive metadata.
    public PhoneFolderEntry OpenRoot() => Folder("", RequireDevice().GetDirectoryInfo(sourcePath));

    private static PhoneFolderEntry Folder(string name, MediaDirectoryInfo directory) =>
        new(name, directory.FullName, () => Enumerate(directory));

    // Files and subfolders share one native directory enumeration, avoiding repeated metadata reads.
    // Downloads reuse the scanned object, which validates fresh metadata before opening a stream.
    private static IEnumerable<PhoneEntry> Enumerate(MediaDirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos("*"))
        {
            yield return entry switch
            {
                MediaFileInfo file => new PhoneFileEntry(file.Name, file.FullName, file.Id, file.PersistentUniqueId,
                    file.Length, file.CreationTime, file.LastWriteTime, file.CopyTo),
                MediaDirectoryInfo folder => Folder(folder.Name, folder),
                _ => throw new IOException($"Unsupported source entry: {entry.FullName}."),
            };
        }
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
}
