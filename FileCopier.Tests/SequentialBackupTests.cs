using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class SequentialBackupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FileCopier.Sequential.Tests", Guid.NewGuid().ToString("N"));
    private readonly DateTime modified = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Local);

    public SequentialBackupTests() => Directory.CreateDirectory(root);

    [Fact]
    public void SkippedAndFailedTransfersDoNotShiftLaterCallbackOrDestination()
    {
        var downloads = new List<string>();
        var sources = new[]
        {
            new PhotoSource("skip", "skip.HEIC", [9, 9, 9], 3, downloads),
            new PhotoSource("sidecar", "sidecar.AAE", [1, 2], 2, downloads),
            new PhotoSource("mismatch", "mismatch.HEIC", [1, 2], 5, downloads),
            new PhotoSource("next", "next.MOV", [3, 4, 5, 6, 7, 8, 9], 7, downloads)
        };
        var skippedPath = Path.Combine(root, sources[0].RelativePath);
        File.WriteAllBytes(skippedPath, sources[0].Contents);
        File.SetLastWriteTime(skippedPath, modified);
        var preservedPath = Path.Combine(root, sources[2].RelativePath);
        byte[] originalBackup = [8, 8, 8, 8];
        File.WriteAllBytes(preservedPath, originalBackup);

        var plan = CreatePlan(sources);
        var skipped = new List<string>();
        var copied = new List<string>();
        var failed = new List<string>();
        foreach (var file in plan)
        {
            Assert.Same(sources.Single(source => source.ObjectId == file.ObjectId), file.Download.Target);
            if (!BackupFile.ShouldCopy(file.DestinationPath, file.Length, file.LastWriteTime))
            {
                skipped.Add(file.ObjectId);
                continue;
            }

            try
            {
                BackupFile.Copy(file.DestinationPath, file.Length, file.CreationTime, file.LastWriteTime, file.Download);
                copied.Add(file.ObjectId);
            }
            catch (IOException)
            {
                failed.Add(file.ObjectId);
            }
        }

        Assert.Equal(["skip"], skipped);
        Assert.Equal(["sidecar", "next"], copied);
        Assert.Equal(["mismatch"], failed);
        Assert.Equal(["sidecar", "mismatch", "next"], downloads);
        Assert.Equal(sources[0].Contents, File.ReadAllBytes(skippedPath));
        Assert.Equal(sources[1].Contents, File.ReadAllBytes(Path.Combine(root, sources[1].RelativePath)));
        Assert.Equal(originalBackup, File.ReadAllBytes(preservedPath));
        Assert.Equal(sources[3].Contents, File.ReadAllBytes(Path.Combine(root, sources[3].RelativePath)));
        Assert.Equal(4, Directory.GetFiles(root).Length);
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public void ConsecutiveEqualLengthFilesKeepTheirOwnPayloads()
    {
        var downloads = new List<string>();
        var sources = new[]
        {
            new PhotoSource("first", "first.HEIC", [1, 1, 1, 1], 4, downloads),
            new PhotoSource("second", "second.HEIC", [2, 2, 2, 2], 4, downloads)
        };

        foreach (var file in CreatePlan(sources))
        {
            Assert.Same(sources.Single(source => source.ObjectId == file.ObjectId), file.Download.Target);
            BackupFile.Copy(file.DestinationPath, file.Length, file.CreationTime, file.LastWriteTime, file.Download);
        }

        Assert.Equal(["first", "second"], downloads);
        Assert.Equal(sources[0].Contents, File.ReadAllBytes(Path.Combine(root, sources[0].RelativePath)));
        Assert.Equal(sources[1].Contents, File.ReadAllBytes(Path.Combine(root, sources[1].RelativePath)));
        Assert.Equal(2, Directory.GetFiles(root).Length);
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }

    private List<SourceFile> CreatePlan(IEnumerable<PhotoSource> sources)
    {
        var files = new List<SourceFile>();
        foreach (var source in sources)
        {
            // Match the app's bound instance-method callback construction.
            files.Add(new SourceFile(source.ObjectId, "persistent-" + source.ObjectId,
                @"\Internal Storage\" + source.RelativePath, source.RelativePath,
                BackupFile.GetDestinationPath(root, source.RelativePath), source.AdvertisedLength,
                null, modified, source.CopyTo));
        }
        return BackupPlan.Create(root, files);
    }

    private sealed class PhotoSource(
        string objectId,
        string relativePath,
        byte[] contents,
        ulong advertisedLength,
        List<string> downloads)
    {
        public string ObjectId { get; } = objectId;
        public string RelativePath { get; } = relativePath;
        public byte[] Contents { get; } = contents;
        public ulong AdvertisedLength { get; } = advertisedLength;

        public void CopyTo(Stream destination)
        {
            downloads.Add(ObjectId);
            destination.Write(Contents);
        }
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
