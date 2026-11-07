using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class BackupFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FileCopier.Tests", Guid.NewGuid().ToString("N"));

    public BackupFileTests() => Directory.CreateDirectory(root);

    [Fact]
    public void InterruptedReplacementPreservesExistingBackupAndCleansTemporaryFile()
    {
        var destination = Path.Combine(root, "photo.jpg");
        File.WriteAllText(destination, "original photo");
        var failure = new IOException("Phone disconnected.");

        var error = Assert.Throws<IOException>(() => BackupFile.Copy(destination, 10, null, null, stream =>
        {
            stream.Write([1, 2, 3]);
            throw failure;
        }));

        Assert.Same(failure, error);
        Assert.Equal("original photo", File.ReadAllText(destination));
        Assert.Equal([destination], Directory.GetFiles(root));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void WrongDownloadLengthPreservesExistingBackup(int actualLength)
    {
        var destination = Path.Combine(root, "photo.jpg");
        File.WriteAllText(destination, "original photo");

        Assert.Throws<IOException>(() => BackupFile.Copy(destination, 3, null, null,
            stream => stream.Write(new byte[actualLength])));

        Assert.Equal("original photo", File.ReadAllText(destination));
        Assert.Equal([destination], Directory.GetFiles(root));
    }

    [Fact]
    public void FailedNewDownloadLeavesNoFinalOrTemporaryFile()
    {
        var destination = Path.Combine(root, "new-photo.jpg");

        Assert.Throws<IOException>(() => BackupFile.Copy(destination, 10, null, null,
            stream => stream.Write([1])));

        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void InvalidTimestampPreservesExistingBackupAndCleansTemporaryFile()
    {
        var destination = Path.Combine(root, "photo.jpg");
        File.WriteAllText(destination, "original photo");

        Assert.ThrowsAny<ArgumentException>(() => BackupFile.Copy(destination, 3, null, DateTime.MinValue,
            stream => stream.Write([1, 2, 3])));

        Assert.Equal("original photo", File.ReadAllText(destination));
        Assert.Equal([destination], Directory.GetFiles(root));
    }

    [Fact]
    public void SuccessfulReplacementWorksWithoutOptionalTimestamps()
    {
        var destination = Path.Combine(root, "photo.jpg");
        File.WriteAllText(destination, "original photo");

        BackupFile.Copy(destination, 3, null, null, stream => stream.Write([1, 2, 3]));

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(root));
        Assert.False(BackupFile.ShouldCopy(destination, 3, null));
    }

    [Fact]
    public void FailedReplacementPreservesLockedBackupAndCleansTemporaryFile()
    {
        var destination = Path.Combine(root, "photo.jpg");
        File.WriteAllText(destination, "original photo");

        using (var lockedBackup = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Assert.ThrowsAny<Exception>(() => BackupFile.Copy(destination, 3, null, null,
                stream => stream.Write([1, 2, 3])));
            Assert.True(error is IOException or UnauthorizedAccessException, error.ToString());
        }

        Assert.Equal("original photo", File.ReadAllText(destination));
        Assert.Equal([destination], Directory.GetFiles(root));
    }

    [Fact]
    public void NewNestedBackupPreservesAvailableTimestamps()
    {
        var destination = Path.Combine(root, "100APPLE", "photo.jpg");
        var created = new DateTime(2025, 3, 4, 10, 20, 30, DateTimeKind.Local);
        var modified = created.AddDays(2);

        BackupFile.Copy(destination, 3, created, modified, stream => stream.Write([1, 2, 3]));

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destination));
        Assert.Equal(created, File.GetCreationTime(destination));
        Assert.Equal(modified, File.GetLastWriteTime(destination));
        Assert.False(BackupFile.ShouldCopy(destination, 3, modified));
        Assert.True(BackupFile.ShouldCopy(destination, 3, modified.AddSeconds(1)));
        Assert.True(BackupFile.ShouldCopy(destination, 4, modified));
    }

    [Fact]
    public void MissingDestinationMustBeCopied()
    {
        Assert.True(BackupFile.ShouldCopy(Path.Combine(root, "missing.jpg"), 0, null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 3000000)]
    [InlineData(1000, 3000000)]
    [InlineData(999, 1002)]
    public void EmptyOrTruncatedBackupIsRetriedDespiteMatchingTimestamp(int existingLength, int sourceLength)
    {
        var destination = Path.Combine(root, "backup.HEIC");
        File.WriteAllBytes(destination, new byte[existingLength]);
        var modified = File.GetLastWriteTime(destination);

        Assert.True(BackupFile.ShouldCopy(destination, (ulong)sourceLength, modified));
    }

    [Fact]
    public void SmallMatchingSidecarIsSkipped()
    {
        var destination = Path.Combine(root, "IMG_0001.AAE");
        File.WriteAllBytes(destination, new byte[834]);

        Assert.False(BackupFile.ShouldCopy(destination, 834, File.GetLastWriteTime(destination)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySourceCannotPublishOrReplaceBackup(bool existingBackup)
    {
        var destination = Path.Combine(root, "nested", "video.MOV");
        if (existingBackup)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, "preserved backup");
        }
        var downloadCalled = false;

        Assert.Throws<IOException>(() => BackupFile.Copy(destination, 0, null, null,
            _ => downloadCalled = true));

        Assert.False(downloadCalled);
        if (existingBackup)
        {
            Assert.Equal("preserved backup", File.ReadAllText(destination));
            Assert.Equal([destination], Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        else
        {
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
    }

    [Theory]
    [InlineData("100APPLE\\photo.jpg")]
    [InlineData("DCIM/100APPLE/photo.jpg")]
    [InlineData("photo.jpg")]
    public void ValidPhonePathStaysWithinDestination(string relativePath)
    {
        var destination = BackupFile.GetDestinationPath(root, relativePath);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', '\\'))), destination);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\photo.jpg")]
    [InlineData("folder/../../photo.jpg")]
    [InlineData("folder\\.\\photo.jpg")]
    [InlineData("C:\\photo.jpg")]
    [InlineData("C:photo.jpg")]
    [InlineData("\\\\server\\share\\photo.jpg")]
    [InlineData("/photo.jpg")]
    [InlineData("folder\\photo.jpg:stream")]
    [InlineData("folder\\bad?name.jpg")]
    [InlineData("folder\\bad*.jpg")]
    [InlineData("folder\\photo.jpg.")]
    [InlineData("folder\\photo.jpg ")]
    [InlineData("folder\\NUL.jpg")]
    [InlineData("folder\\com1.jpg")]
    [InlineData("folder\\LPT¹.jpg")]
    [InlineData("folder\\CON .jpg")]
    [InlineData("folder\\CONIN$")]
    [InlineData("folder//photo.jpg")]
    public void MalformedPhonePathIsRejectedBeforeTouchingDisk(string relativePath)
    {
        Assert.ThrowsAny<ArgumentException>(() => BackupFile.GetDestinationPath(root, relativePath));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void VerifyAcceptsAnIdenticalBackupSentInChunks()
    {
        var destination = Backup("same.MOV", [1, 2, 3, 4, 5]);

        Assert.Null(BackupFile.Verify(destination, 5, stream =>
        {
            stream.Write([1, 2]);
            stream.Write([3, 4, 5]);
        }));
        Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(destination));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    public void VerifyReportsTheFirstDifferentByte(int changedIndex, int expectedOffset)
    {
        var destination = Backup("changed.HEIC", [1, 2, 3, 4, 5]);
        byte[] phone = [1, 2, 3, 4, 5];
        phone[changedIndex] = 9;

        var difference = BackupFile.Verify(destination, 5, stream => stream.Write(phone));

        Assert.Equal($"Backup copy differs from the iPhone at byte {expectedOffset}. Delete the backup copy, then copy again.", difference);
        Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(destination));
    }

    [Fact]
    public void VerifyReportsMissingOrResizedBackupWithoutReadingThePhone()
    {
        var reads = 0;
        var resized = Backup("short.JPG", [1, 2]);

        Assert.StartsWith("Missing from the backup.", BackupFile.Verify(Path.Combine(root, "missing.JPG"), 3, _ => reads++));
        Assert.StartsWith("Backup copy has 2 bytes; the iPhone has 3.", BackupFile.Verify(resized, 3, _ => reads++));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void VerifyTreatsAShortPhoneReadAsAFailedTransfer()
    {
        var destination = Backup("cut.MOV", [1, 2, 3]);

        Assert.Throws<IOException>(() => BackupFile.Verify(destination, 3, stream => stream.Write([1, 2])));
    }

    [Fact]
    public void VerifyRestartsTheComparisonWhenADownloadIsRetried()
    {
        var destination = Backup("retry.MOV", [1, 2, 3, 4]);

        // A failed attempt sent a wrong byte, then the source rewound the stream and resent the file.
        var difference = BackupFile.Verify(destination, 4, stream =>
        {
            var start = stream.Position;
            stream.Write([1, 7]);
            stream.SetLength(start);
            stream.Position = start;
            stream.Write([1, 2, 3, 4]);
        });

        Assert.Null(difference);
    }

    [Fact]
    public void VerifyRejectsAnEmptyPhoneFile()
    {
        var destination = Backup("empty.MOV", [1]);

        Assert.Throws<IOException>(() => BackupFile.Verify(destination, 0, _ => { }));
    }

    private string Backup(string name, byte[] content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
