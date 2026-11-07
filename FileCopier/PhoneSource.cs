namespace FileCopier;

internal abstract record PhoneEntry(string Name, string PhonePath);

/// <summary>A phone folder. <see cref="Enumerate"/> lists its entries lazily, in one device listing.</summary>
internal sealed record PhoneFolderEntry(string Name, string PhonePath, Func<IEnumerable<PhoneEntry>> Enumerate)
    : PhoneEntry(Name, PhonePath);

internal sealed record PhoneFileEntry(
    string Name,
    string PhonePath,
    string ObjectId,
    string PersistentId,
    ulong Length,
    DateTime? CreationTime,
    DateTime? LastWriteTime,
    Action<Stream> Download) : PhoneEntry(Name, PhonePath);

/// <summary>A way of reading the iPhone's photos: WPD (Windows Portable Devices) or AFC.</summary>
internal interface IPhoneSource : IDisposable
{
    string PhoneName { get; }
    string SourcePath { get; }

    /// <summary>
    /// True when a failed file cannot affect later transfers, so copying may continue with the next file.
    /// </summary>
    bool IsolatesFailures { get; }

    void Connect();
    void Diagnose();
    PhoneFolderEntry OpenRoot();
}
