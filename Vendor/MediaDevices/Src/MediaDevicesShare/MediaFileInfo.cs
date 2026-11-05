namespace MediaDevices;

/// <summary>
/// Provides properties for files, directories and objects.
/// </summary>
public partial class MediaFileInfo : MediaFileSystemInfo
{
    private readonly MediaFileSnapshot scannedSnapshot;

    internal MediaFileInfo(MediaDevice device, Item item) : base(device, item)
    {
        scannedSnapshot = MediaFileSnapshot.Capture(item);
    }

    /// <summary>
    /// Refreshes the state of the object.
    /// </summary>
    public override void Refresh() => base.Refresh();
    
    /// <summary>
    /// Gets an instance of the parent directory.
    /// </summary>
    public MediaDirectoryInfo? Directory => base.ParentDirectoryInfo;

    /// <summary>
    /// Copies an existing file to a new file, allowing the overwriting of the existing file.
    /// </summary>
    /// <param name="destFileName">The name of the new file to copy to.</param>
    /// <param name="overwrite">true to allow an existing file to be overwritten; otherwise, false. </param>
    /// <exception cref="System.IO.IOException">An error occurs, or the destination file already exists and overwrite is false. </exception>
    /// <exception cref="System.IO.DirectoryNotFoundException">path is invalid.</exception>
    /// <exception cref="MediaDevices.NotConnectedException">mediaDevice is not connected.</exception>
    public void CopyTo(string destFileName, bool overwrite = true)
    {
        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        mainWorker.Invoke(() =>
        {
            RefreshAndValidateSource();
            ProtocolHandler.CopyTo(item, destFileName, overwrite);
        });
    }

    /// <summary>
    /// Copies this file to a writable stream without resolving its device path again.
    /// The destination remains open, and native stream operations run on the device worker.
    /// </summary>
    /// <param name="destination">The stream to write to at its current position.</param>
    /// <exception cref="ArgumentNullException">destination is null.</exception>
    /// <exception cref="ArgumentException">destination is not writable.</exception>
    /// <exception cref="IOException">The file cannot be read or the destination cannot be written.</exception>
    /// <exception cref="NotConnectedException">The media device is not connected.</exception>
    public void CopyTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));

        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        mainWorker.Invoke(() =>
        {
            RefreshAndValidateSource();
            ProtocolHandler.CopyTo(item, destination);
        });
    }

    private void RefreshAndValidateSource()
    {
        item.Refresh();
        scannedSnapshot.EnsureUnchanged(MediaFileSnapshot.Capture(item));
    }

    /// <summary>
    /// Copies an icon of an existing file to a new file, allowing the overwriting of the existing file.
    /// </summary>
    /// <param name="destFileName">The name of the new file to copy to.</param>
    /// <param name="overwrite">true to allow an existing file to be overwritten; otherwise, false. </param>
    /// <exception cref="System.IO.IOException">An error occurs, or the destination file already exists and overwrite is false. </exception>
    /// <exception cref="System.IO.DirectoryNotFoundException">path is invalid.</exception>
    /// <exception cref="MediaDevices.NotConnectedException">mediaDevice is not connected.</exception>
    public void CopyIconTo(string destFileName, bool overwrite = true)
    {
        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        mainWorker.Invoke(() => ProtocolHandler.CopyIconTo(item, destFileName, overwrite));
    }

    /// <summary>
    /// Copies an thumbnail of an existing file to a new file, allowing the overwriting of the existing file.
    /// </summary>
    /// <param name="destFileName">The name of the new file to copy to.</param>
    /// <param name="overwrite">true to allow an existing file to be overwritten; otherwise, false. </param>
    /// <exception cref="System.IO.IOException">An error occurs, or the destination file already exists and overwrite is false. </exception>
    /// <exception cref="System.IO.DirectoryNotFoundException">path is invalid.</exception>
    /// <exception cref="MediaDevices.NotConnectedException">mediaDevice is not connected.</exception>
    public void CopyThumbnailTo(string destFileName, bool overwrite = true)
    {
        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        mainWorker.Invoke(() => ProtocolHandler.CopyThumbnailTo(item, destFileName, overwrite));
    }

    /// <summary>
    /// Creates a read-only FileStream.
    /// </summary>
    /// <returns>A new read-only FileStream object.</returns>
    /// <exception cref="System.IO.DirectoryNotFoundException">path is invalid.</exception>
    /// <exception cref="MediaDevices.NotConnectedException">mediaDevice is not connected.</exception>
    public Stream OpenRead()
    {
        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        return mainWorker.Invoke(() => ProtocolHandler.OpenRead(item));
    }

    /// <summary>
    /// Creates a read-only FileStream of the icon.
    /// </summary>
    /// <returns>A new read-only FileStream object.</returns>
    /// <exception cref="System.IO.DirectoryNotFoundException">path is invalid.</exception>
    /// <exception cref="MediaDevices.NotConnectedException">mediaDevice is not connected.</exception>
    public Stream OpenIcon()
    {
        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        return mainWorker.Invoke(() => ProtocolHandler.OpenIcon(item));
    }

    /// <summary>
    /// Creates a read-only FileStream of the thumbnail.
    /// </summary>
    /// <returns>A new read-only FileStream object.</returns>
    /// <exception cref="System.IO.DirectoryNotFoundException">path is invalid.</exception>
    /// <exception cref="MediaDevices.NotConnectedException">mediaDevice is not connected.</exception>
    public Stream OpenThumbnail()
    {
        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        return mainWorker.Invoke(() => ProtocolHandler.OpenThumbnail(item));
    }

    /// <summary>
    /// Creates a StreamReader with UTF8 encoding that reads from an existing text file.
    /// </summary>
    /// <returns>A new StreamReader with UTF8 encoding.</returns>
    /// <exception cref="System.IO.DirectoryNotFoundException">path is invalid.</exception>
    /// <exception cref="MediaDevices.NotConnectedException">mediaDevice is not connected.</exception>
    public StreamReader OpenText()
    {
        NotConnectedException.ThrowIfNotConnected(this.mediaDevice);
        return mainWorker.Invoke(() => ProtocolHandler.OpenText(item));
    }
}

// Keep the scan identity immutable even if Refresh detects a changed device view.
// A retry on the same object must not silently adopt another photo's identity.
internal readonly record struct MediaFileSnapshot(
    string Id,
    string PersistentUniqueId,
    string Name,
    ulong Length,
    DateTime? CreationTime,
    DateTime? LastWriteTime)
{
    internal static MediaFileSnapshot Capture(Item item) => new(
        item.Id, item.PersistentUniqueId, item.Name, item.Size, item.DateCreated, item.DateModified);

    internal void EnsureUnchanged(MediaFileSnapshot current)
    {
        if (this != current)
            throw new IOException($"iPhone source metadata changed since scanning '{Name}' (object {Id}). Reconnect the phone and rescan before copying.");
    }
}
