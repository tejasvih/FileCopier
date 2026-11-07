using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class PhoneBackupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FileCopier.PhoneBackup.Tests", Guid.NewGuid().ToString("N"));
    public PhoneBackupTests() => Directory.CreateDirectory(root);

    [Fact]
    public void IsolatingSourceContinuesPastAFailedFile()
    {
        var source = new FakeSource(isolatesFailures: true,
            Photo("IMG_0001.HEIC", [1, 2, 3]), Failing("IMG_0002.HEIC"), Photo("IMG_0003.HEIC", [4, 5]));
        var backup = Run(source);

        Assert.Equal(2, backup.CopiedFiles);
        Assert.Single(backup.Errors);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(root, "100APPLE", "IMG_0001.HEIC")));
        Assert.Equal([4, 5], File.ReadAllBytes(Path.Combine(root, "100APPLE", "IMG_0003.HEIC")));
        Assert.False(File.Exists(Path.Combine(root, "100APPLE", "IMG_0002.HEIC")));
    }

    [Fact]
    public void IsolatingSourceStopsAfterConsecutiveFailures()
    {
        var files = Enumerable.Range(1, PhoneBackup.MaximumConsecutiveFailures)
            .Select(index => Failing($"IMG_000{index}.HEIC"))
            .Append(Photo("IMG_0009.HEIC", [9]))
            .ToArray();
        var source = new FakeSource(isolatesFailures: true, files);

        Assert.Throws<IOException>(() => Run(source));
        Assert.Equal(PhoneBackup.MaximumConsecutiveFailures, source.Downloads.Count);
    }

    [Fact]
    public void NonIsolatingSourceStopsAtFirstFailure()
    {
        var source = new FakeSource(isolatesFailures: false,
            Failing("IMG_0001.HEIC"), Photo("IMG_0002.HEIC", [1]));

        Assert.Throws<IOException>(() => Run(source));
        Assert.Equal(["IMG_0001.HEIC"], source.Downloads);
    }

    [Fact]
    public void ScanOnlyPreviewReadsNoFileContents()
    {
        WriteBackup(Path.Combine("100APPLE", "IMG_6399.AAE"), [1, 2, 3]);
        var source = new FakeSource(isolatesFailures: true,
            Photo("IMG_6399.AAE", [1, 2, 3]), Photo("IMG_6400.HEIC", [4]), Photo("IMG_6401.MOV", []));

        var backup = Run(source, scanOnly: true);

        Assert.Equal(3, backup.FoundFiles);
        Assert.Equal(1, backup.EmptyFiles);
        Assert.Empty(backup.Errors);
        Assert.Empty(source.Downloads);
        Assert.Equal(0, backup.CopiedFiles);
    }

    [Fact]
    public void RerunSkipsFilesAlreadyCopied()
    {
        var first = new FakeSource(isolatesFailures: true, Photo("IMG_0001.HEIC", [1, 2, 3]));
        Assert.Equal(1, Run(first).CopiedFiles);

        var second = new FakeSource(isolatesFailures: true, Photo("IMG_0001.HEIC", [1, 2, 3]));
        var rerun = Run(second);

        Assert.Equal(0, rerun.CopiedFiles);
        Assert.Equal(1, rerun.SkippedFiles);
        Assert.Empty(second.Downloads);
    }

    [Fact]
    public void EmptyFileOnExactSizeSourceIsReportedWithoutAnError()
    {
        var source = new FakeSource(isolatesFailures: true,
            Photo("IMG_3941.MOV", []), Photo("IMG_3942.HEIC", [7]));

        var backup = Run(source);

        Assert.Empty(backup.Errors);
        Assert.Equal(1, backup.EmptyFiles);
        Assert.Equal(1, backup.CopiedFiles);
        Assert.Equal(["IMG_3942.HEIC"], source.Downloads);
        Assert.False(File.Exists(Path.Combine(root, "100APPLE", "IMG_3941.MOV")));
    }

    [Fact]
    public void EmptyFileOnInexactSizeSourceIsAnError()
    {
        var source = new FakeSource(isolatesFailures: true, Photo("IMG_3941.MOV", []))
        {
            ReportsExactSizes = false,
        };

        var backup = Run(source);

        Assert.Single(backup.Errors);
        Assert.Equal(0, backup.EmptyFiles);
        Assert.False(File.Exists(Path.Combine(root, "100APPLE", "IMG_3941.MOV")));
    }

    [Fact]
    public void VerifyReportsDifferentAndMissingCopiesAndWritesNothing()
    {
        WriteBackup(Path.Combine("100APPLE", "IMG_0001.HEIC"), [1, 2, 3]);
        WriteBackup(Path.Combine("100APPLE", "IMG_0002.HEIC"), [4, 5, 6]);
        var source = new FakeSource(isolatesFailures: true,
            Photo("IMG_0001.HEIC", [1, 2, 3]), Photo("IMG_0002.HEIC", [4, 0, 6]),
            Photo("IMG_0003.HEIC", [7]), Photo("IMG_0004.MOV", []));

        var backup = Verify(source);

        Assert.Equal(1, backup.VerifiedFiles);
        Assert.Equal(1, backup.EmptyFiles);
        Assert.Equal(["/DCIM/100APPLE/IMG_0002.HEIC", "/DCIM/100APPLE/IMG_0003.HEIC"], backup.Errors.Select(error => error.Path));
        Assert.Equal(["IMG_0001.HEIC", "IMG_0002.HEIC"], source.Downloads);
        Assert.Equal([4, 5, 6], File.ReadAllBytes(Path.Combine(root, "100APPLE", "IMG_0002.HEIC")));
        Assert.False(File.Exists(Path.Combine(root, "100APPLE", "IMG_0003.HEIC")));
    }

    [Fact]
    public void VerifyContinuesPastAFailedReadOnAnIsolatingSource()
    {
        WriteBackup(Path.Combine("100APPLE", "IMG_0001.HEIC"), [1, 2, 3, 4]);
        WriteBackup(Path.Combine("100APPLE", "IMG_0002.HEIC"), [5]);
        var source = new FakeSource(isolatesFailures: true, Failing("IMG_0001.HEIC"), Photo("IMG_0002.HEIC", [5]));

        var backup = Verify(source);

        Assert.Single(backup.Errors);
        Assert.Equal(1, backup.VerifiedFiles);
    }

    [Fact]
    public void ProgressCountsFilesAndFoldersOfThePlan()
    {
        var source = new FakeSource(isolatesFailures: true, Photo("IMG_0001.HEIC", [1]), Photo("IMG_0002.HEIC", [2]))
        {
            SecondFolder = [Photo("IMG_0003.HEIC", [3])],
        };

        var backup = Run(source);

        // The scanner reaches the last listed folder first, so 101APPLE is folder 1.
        Assert.EndsWith("File 1/3, folder 1/2", backup.Progress(0));
        Assert.EndsWith("File 2/3, folder 2/2", backup.Progress(1));
        Assert.EndsWith("File 3/3, folder 2/2", backup.Progress(2));
    }

    private PhoneBackup Verify(FakeSource source)
    {
        var backup = new PhoneBackup(source, root);
        backup.VerifyBackup(CancellationToken.None);
        return backup;
    }

    private PhoneBackup Run(FakeSource source, bool scanOnly = false)
    {
        var backup = new PhoneBackup(source, root);
        backup.CopyPhotos(CancellationToken.None, scanOnly);
        return backup;
    }

    private void WriteBackup(string relativePath, byte[] content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private (string Name, byte[]? Content) Photo(string name, byte[] content) => (name, content);
    private (string Name, byte[]? Content) Failing(string name) => (name, null);

    private sealed class FakeSource(bool isolatesFailures, params (string Name, byte[]? Content)[] files)
        : IPhoneSource
    {
        private readonly DateTime modified = new(2026, 7, 4, 12, 56, 7, DateTimeKind.Local);

        public List<string> Downloads { get; } = [];
        public string PhoneName => "Test iPhone";
        public string SourcePath => "/DCIM";
        public bool IsolatesFailures => isolatesFailures;
        public bool ReportsExactSizes { get; init; } = true;
        public void Connect() { }
        public void Diagnose() { }
        public void Dispose() { }

        public (string Name, byte[]? Content)[] SecondFolder { get; init; } = [];

        public PhoneFolderEntry OpenRoot() => new("", "/DCIM", () =>
            SecondFolder.Length == 0
                ? [Folder("100APPLE", files)]
                : [Folder("100APPLE", files), Folder("101APPLE", SecondFolder)]);

        private PhoneFolderEntry Folder(string name, (string Name, byte[]? Content)[] contents) =>
            new(name, $"/DCIM/{name}", () => contents.Select(file => File(name, file)));

        private PhoneEntry File(string folder, (string Name, byte[]? Content) file)
        {
            var path = $"/DCIM/{folder}/{file.Name}";
            return new PhoneFileEntry(file.Name, path, path, path, (ulong)(file.Content?.Length ?? 4),
                modified, modified, destination =>
                {
                    Downloads.Add(file.Name);
                    if (file.Content == null) throw new IOException("device I/O error");
                    destination.Write(file.Content);
                });
        }
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
