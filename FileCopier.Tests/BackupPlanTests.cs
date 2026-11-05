using System.Security.Cryptography;
using System.Text;
using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class BackupPlanTests
{
    private readonly string destinationRoot = Path.Combine(Path.GetTempPath(), "FileCopier.Plan.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DistinctObjectsWithSameFilenameReceiveStablePathsAndKeepTheirOwnDownloads()
    {
        var first = CreateFile("native-1", "persistent-1", @"202605_a\IMG_6746.MOV", [1, 2]);
        var second = CreateFile("native-2", "persistent-2", @"202605_a\IMG_6746.MOV", [3, 4, 5]);

        var plan = BackupPlan.Create(destinationRoot, [first, second], _ => false);
        var reversed = BackupPlan.Create(destinationRoot, [second, first], _ => false);

        Assert.Equal(2, plan.Count);
        Assert.Equal(2, plan.Select(file => file.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(plan, file => Assert.NotEqual(first.DestinationPath, file.DestinationPath));
        foreach (var file in plan)
        {
            var corresponding = Assert.Single(reversed, other => other.ObjectId == file.ObjectId);
            Assert.Equal(file.DestinationPath, corresponding.DestinationPath);
            Assert.EndsWith(".MOV", file.DestinationPath);
            Assert.Same(file.ObjectId == first.ObjectId ? first.Download : second.Download, file.Download);
            using var stream = new MemoryStream();
            file.Download(stream);
            Assert.Equal(file.ObjectId == first.ObjectId ? new byte[] { 1, 2 } : [3, 4, 5], stream.ToArray());
        }
        Assert.False(Directory.Exists(destinationRoot));
    }

    [Fact]
    public void CollidingObjectsNeverUseOrModifyExistingAmbiguousOriginal()
    {
        var first = CreateFile("native-1", "persistent-1");
        var second = CreateFile("native-2", "persistent-2");
        var queried = new List<string>();

        var plan = BackupPlan.Create(destinationRoot, [first, second], path =>
        {
            queried.Add(path);
            return path.Equals(first.DestinationPath, StringComparison.OrdinalIgnoreCase);
        });

        Assert.All(plan, file => Assert.NotEqual(first.DestinationPath, file.DestinationPath));
        Assert.Empty(queried);
        Assert.False(Directory.Exists(destinationRoot));
    }

    [Fact]
    public void ConsistentRepeatedNativeObjectIsDeduplicated()
    {
        var first = CreateFile("native-1", "persistent-1");
        var repeated = first with { Download = _ => throw new InvalidOperationException("Use the first object's callback.") };

        var planned = Assert.Single(BackupPlan.Create(destinationRoot, [first, repeated], _ => false));

        Assert.Equal(first.DestinationPath, planned.DestinationPath);
        Assert.Same(first.Download, planned.Download);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("created")]
    [InlineData("modified")]
    [InlineData("persistent-id")]
    [InlineData("phone-path")]
    [InlineData("relative-path")]
    public void ContradictoryRepeatedNativeObjectFailsBeforeAnyDownload(string changedMetadata)
    {
        var first = CreateFile("native-1", "persistent-1");
        var repeated = changedMetadata switch
        {
            "length" => first with { Length = first.Length + 1 },
            "created" => first with { CreationTime = first.CreationTime!.Value.AddSeconds(1) },
            "modified" => first with { LastWriteTime = first.LastWriteTime!.Value.AddSeconds(1) },
            "persistent-id" => first with { PersistentId = "different-persistent-id" },
            "phone-path" => first with { PhonePath = @"\Internal Storage\another.jpg" },
            "relative-path" => first with { RelativePath = "another.jpg" },
            _ => throw new InvalidOperationException()
        };

        Assert.Throws<IOException>(() => BackupPlan.Create(destinationRoot, [first, repeated], _ => false));
        Assert.False(Directory.Exists(destinationRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("persistent-1")]
    public void CollisionWithoutDistinctPersistentIdsFailsClosed(string secondPersistentId)
    {
        var first = CreateFile("native-1", "persistent-1");
        var second = CreateFile("native-2", secondPersistentId);

        Assert.Throws<IOException>(() => BackupPlan.Create(destinationRoot, [first, second], _ => false));
    }

    [Fact]
    public void RemainingObjectKeepsItsExistingStablePathWhenCollisionGroupShrinks()
    {
        var first = CreateFile("native-1", "persistent-1");
        var second = CreateFile("native-2", "persistent-2");
        var previousPlan = BackupPlan.Create(destinationRoot, [first, second], _ => false);
        var existingPaths = previousPlan.Select(file => file.DestinationPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var remaining = Assert.Single(BackupPlan.Create(destinationRoot, [first], existingPaths.Contains));

        Assert.Equal(previousPlan.Single(file => file.ObjectId == first.ObjectId).DestinationPath, remaining.DestinationPath);
        Assert.NotEqual(first.DestinationPath, remaining.DestinationPath);
    }

    [Fact]
    public void UnambiguousObjectKeepsOriginalNameAndCanLackPersistentId()
    {
        var source = CreateFile("native-1", "");

        var planned = Assert.Single(BackupPlan.Create(destinationRoot, [source], _ => false));

        Assert.Equal(source.DestinationPath, planned.DestinationPath);
    }

    [Fact]
    public void GeneratedNameCannotOverwriteAnotherNativeFilename()
    {
        var first = CreateFile("native-1", "persistent-1");
        var second = CreateFile("native-2", "persistent-2");
        var collisionPlan = BackupPlan.Create(destinationRoot, [first, second], _ => false);
        var generatedRelativePath = Path.GetRelativePath(destinationRoot, collisionPlan[0].DestinationPath);
        var third = CreateFile("native-3", "persistent-3", generatedRelativePath);

        Assert.Throws<IOException>(() => BackupPlan.Create(destinationRoot, [first, second, third], _ => false));
    }

    [Fact]
    public void CaseInsensitiveNativeFilenameCollisionsArePreserved()
    {
        var first = CreateFile("native-1", "persistent-1", @"202605_a\IMG_6746.MOV");
        var second = CreateFile("native-2", "persistent-2", @"202605_A\img_6746.mov");

        var plan = BackupPlan.Create(destinationRoot, [first, second], _ => false);

        Assert.Equal(2, plan.Select(file => file.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(plan, file => Assert.Contains("__", Path.GetFileName(file.DestinationPath)));
    }

    [Fact]
    public void LongNamesRetainExtensionAndDoNotSplitSurrogatePair()
    {
        // The natural truncation point falls between this emoji's UTF-16 surrogates.
        var name = new string('a', 232) + "😀" + new string('b', 15) + ".MOV";
        var first = CreateFile("native-1", "persistent-1", name);
        var second = CreateFile("native-2", "persistent-2", name);

        var plan = BackupPlan.Create(destinationRoot, [first, second], _ => false);

        Assert.All(plan, file =>
        {
            var plannedName = Path.GetFileName(file.DestinationPath);
            Assert.True(plannedName.Length <= 255);
            Assert.EndsWith(".MOV", plannedName);
            Assert.DoesNotContain('\uD83D', plannedName);
            Assert.DoesNotContain('\uDE00', plannedName);
        });
    }

    [Fact]
    public void PersistentSuffixUsesStablePersistentIdRatherThanVolatileNativeId()
    {
        var first = CreateFile("old-native-1", "persistent-1");
        var second = CreateFile("old-native-2", "persistent-2");
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.PersistentId)))[..16].ToLowerInvariant();

        var oldPlan = BackupPlan.Create(destinationRoot, [first, second], _ => false);
        var newPlan = BackupPlan.Create(destinationRoot,
            [first with { ObjectId = "new-native-1" }, second with { ObjectId = "new-native-2" }], _ => false);

        Assert.Contains("__" + expectedHash + ".MOV", oldPlan[0].DestinationPath);
        Assert.Equal(oldPlan.Select(file => file.DestinationPath), newPlan.Select(file => file.DestinationPath));
    }

    [Fact]
    public void SingletonWithVeryLongExtensionKeepsValidOriginalName()
    {
        var source = CreateFile("native-1", "persistent-1", "a." + new string('b', 253));

        var planned = Assert.Single(BackupPlan.Create(destinationRoot, [source], _ => false));

        Assert.Equal(source.DestinationPath, planned.DestinationPath);
    }

    [Fact]
    public void CollisionWithExtensionTooLongForSuffixFailsWithoutChangingExtension()
    {
        var first = CreateFile("native-1", "persistent-1", "a." + new string('b', 253));
        var second = first with { ObjectId = "native-2", PersistentId = "persistent-2" };

        Assert.Throws<IOException>(() => BackupPlan.Create(destinationRoot, [first, second], _ => false));
    }

    [Fact]
    public void PlannerRebuildsDestinationFromValidatedRelativePath()
    {
        var source = CreateFile("native-1", "persistent-1") with
        {
            DestinationPath = Path.Combine(Path.GetTempPath(), "outside-the-destination.mov")
        };

        var planned = Assert.Single(BackupPlan.Create(destinationRoot, [source], _ => false));

        Assert.Equal(BackupFile.GetDestinationPath(destinationRoot, source.RelativePath), planned.DestinationPath);
        Assert.Throws<ArgumentException>(() => BackupPlan.Create(destinationRoot,
            [source with { RelativePath = @"..\outside.mov" }], _ => false));
    }

    [Fact]
    public void ExclusionsMatchOriginalPhonePathAfterCollisionRenaming()
    {
        var first = CreateFile("native-1", "persistent-1");
        var second = CreateFile("native-2", "persistent-2");
        var plan = BackupPlan.Create(destinationRoot, [first, second], _ => false);

        var exclusions = BackupPlan.ResolveExclusions(plan, ["202605_A/img_6746.mov"]);

        Assert.All(plan, file => Assert.True(exclusions.Contains(file.RelativePath)));
        Assert.All(plan, file => Assert.NotEqual(first.DestinationPath, file.DestinationPath));
        Assert.Equal(2, plan.Count);
        Assert.False(Directory.Exists(destinationRoot));
    }

    [Fact]
    public void UnknownExclusionFailsBeforeAnyDownloadOrDestinationCreation()
    {
        var source = CreateFile("native-1", "persistent-1") with
        {
            Download = _ => throw new InvalidOperationException("Must not download during preflight.")
        };
        var plan = BackupPlan.Create(destinationRoot, [source], _ => false);

        var error = Assert.Throws<ArgumentException>(() => BackupPlan.ResolveExclusions(plan, ["missing.MOV"]));

        Assert.Contains("missing.MOV", error.Message);
        Assert.False(Directory.Exists(destinationRoot));
    }

    [Fact]
    public void NoExclusionsLeaveEveryPlannedFileEligible()
    {
        var plan = BackupPlan.Create(destinationRoot, [CreateFile("native-1", "persistent-1")], _ => false);

        Assert.Empty(BackupPlan.ResolveExclusions(plan, null));
        Assert.Single(plan);
    }

    private SourceFile CreateFile(
        string objectId,
        string persistentId,
        string relativePath = @"202605_a\IMG_6746.MOV",
        byte[]? contents = null)
    {
        contents ??= [1];
        var created = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Local);
        return new SourceFile(objectId, persistentId, @"\Internal Storage\" + relativePath, relativePath,
            BackupFile.GetDestinationPath(destinationRoot, relativePath), (ulong)contents.Length,
            created, created.AddMinutes(1), stream => stream.Write(contents));
    }
}
