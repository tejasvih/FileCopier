using MediaDevices;
using Xunit;

namespace FileCopier.Tests;

public sealed class MediaFileSnapshotTests
{
    private static readonly MediaFileSnapshot Original = new(
        "object-1", "persistent-1", "IMG_0001.HEIC", 1024,
        new DateTime(2026, 1, 2, 3, 4, 5), new DateTime(2026, 2, 3, 4, 5, 6));

    [Fact]
    public void UnchangedMetadataPasses()
    {
        Original.EnsureUnchanged(Original with { });
    }

    [Theory]
    [InlineData("id")]
    [InlineData("persistent-id")]
    [InlineData("name")]
    [InlineData("size")]
    [InlineData("creation")]
    [InlineData("write")]
    [InlineData("missing-persistent-id")]
    [InlineData("missing-creation")]
    [InlineData("missing-write")]
    public void ChangedIdentityOrMetadataFailsBeforeCopy(string change)
    {
        var current = change switch
        {
            "id" => Original with { Id = "object-2" },
            "persistent-id" => Original with { PersistentUniqueId = "persistent-2" },
            "name" => Original with { Name = "IMG_0002.HEIC" },
            "size" => Original with { Length = 2048 },
            "creation" => Original with { CreationTime = Original.CreationTime!.Value.AddSeconds(1) },
            "write" => Original with { LastWriteTime = Original.LastWriteTime!.Value.AddSeconds(1) },
            "missing-persistent-id" => Original with { PersistentUniqueId = "" },
            "missing-creation" => Original with { CreationTime = null },
            "missing-write" => Original with { LastWriteTime = null },
            _ => throw new InvalidOperationException()
        };

        Assert.Throws<IOException>(() => Original.EnsureUnchanged(current));
    }

    [Fact]
    public void EqualSizeDifferentPersistentIdentityStillFails()
    {
        var differentPhoto = Original with { PersistentUniqueId = "other-photo", Length = Original.Length };

        Assert.Throws<IOException>(() => Original.EnsureUnchanged(differentPhoto));
        Assert.Throws<IOException>(() => Original.EnsureUnchanged(differentPhoto));
        Original.EnsureUnchanged(Original);
    }

    [Fact]
    public void NativeIdentityIsCaseSensitive()
    {
        Assert.Throws<IOException>(() => Original.EnsureUnchanged(Original with { Id = "OBJECT-1" }));
    }
}
