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
            Photo("IMG_6399.AAE", [1, 2, 3]), Photo("IMG_6400.HEIC", [4]));

        var backup = Run(source, scanOnly: true);

        Assert.Equal(2, backup.FoundFiles);
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

    private PhoneBackup Run(FakeSource source, bool scanOnly = false)
    {
        var config = new AppConfig { MinimumExpectedFiles = 0 };
        config.Validate();
        var backup = new PhoneBackup(config, source, root);
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
        public void Connect() { }
        public void Diagnose() { }
        public void Dispose() { }

        public PhoneFolderEntry OpenRoot() => new("", "/DCIM", () =>
            [new PhoneFolderEntry("100APPLE", "/DCIM/100APPLE", () => files.Select(File))]);

        private PhoneEntry File((string Name, byte[]? Content) file)
        {
            var path = $"/DCIM/100APPLE/{file.Name}";
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
