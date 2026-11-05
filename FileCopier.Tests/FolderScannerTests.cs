using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class FolderScannerTests
{
    [Fact]
    public void ScansAll128SiblingFoldersAndSourceRoot()
    {
        var children = Enumerable.Range(1, 128).Select(index => $"folder-{index}").ToArray();
        var scanned = new List<string>();
        var errors = new List<Exception>();

        FolderScanner.Scan("root",
            folder => folder == "root" ? children : [],
            scanned.Add,
            (_, exception) => errors.Add(exception),
            () => { });

        Assert.Empty(errors);
        Assert.Equal(129, scanned.Count);
        Assert.Equal(129, scanned.Distinct().Count());
        Assert.Contains("root", scanned);
        Assert.All(children, child => Assert.Contains(child, scanned));
    }

    [Fact]
    public void FileErrorDoesNotSuppressDescendantsOrSiblingFolders()
    {
        var scanned = new List<string>();
        var errors = new List<(string Folder, Exception Exception)>();
        var failure = new IOException("Cannot read this folder's files.");

        FolderScanner.Scan("root",
            folder => folder switch
            {
                "root" => ["parent", "sibling"],
                "parent" => ["descendant"],
                _ => []
            },
            folder =>
            {
                scanned.Add(folder);
                if (folder == "parent") throw failure;
            },
            (folder, exception) => errors.Add((folder, exception)),
            () => { });

        Assert.Equal(4, scanned.Count);
        Assert.Contains("descendant", scanned);
        Assert.Contains("sibling", scanned);
        var error = Assert.Single(errors);
        Assert.Equal("parent", error.Folder);
        Assert.Same(failure, error.Exception);
    }

    [Fact]
    public void PartialChildEnumerationFailureRetainsDiscoveredChildrenAndScansParentFiles()
    {
        var scanned = new List<string>();
        var errors = new List<(string Folder, Exception Exception)>();
        var failure = new IOException("Child listing interrupted.");

        IEnumerable<string> GetChildren(string folder)
        {
            if (folder != "root") yield break;
            yield return "first-child";
            yield return "second-child";
            throw failure;
        }

        FolderScanner.Scan("root", GetChildren, scanned.Add,
            (folder, exception) => errors.Add((folder, exception)),
            () => { });

        Assert.Equal(3, scanned.Count);
        Assert.Contains("root", scanned);
        Assert.Contains("first-child", scanned);
        Assert.Contains("second-child", scanned);
        var error = Assert.Single(errors);
        Assert.Equal("root", error.Folder);
        Assert.Same(failure, error.Exception);
    }

    [Fact]
    public void CancellationStopsQueuedFoldersAndPropagatesToCaller()
    {
        using var cancellation = new CancellationTokenSource();
        var scanned = new List<string>();
        var errors = new List<Exception>();

        Assert.ThrowsAny<OperationCanceledException>(() => FolderScanner.Scan("root",
            folder => folder == "root" ? ["child"] : [],
            folder =>
            {
                scanned.Add(folder);
                cancellation.Cancel();
            },
            (_, exception) => errors.Add(exception),
            cancellation.Token.ThrowIfCancellationRequested));

        Assert.Equal(["root"], scanned);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationDuringFolderOrFileEnumerationIsNeverReportedAsOrdinaryError(bool duringChildEnumeration)
    {
        var failure = new OperationCanceledException("Stopped by user.");
        var errors = new List<Exception>();
        var scanned = new List<string>();

        var error = Assert.Throws<OperationCanceledException>(() => FolderScanner.Scan("root",
            _ => duringChildEnumeration ? throw failure : ["child"],
            folder =>
            {
                scanned.Add(folder);
                throw failure;
            },
            (_, exception) => errors.Add(exception),
            () => { }));

        Assert.Same(failure, error);
        Assert.Empty(errors);
        Assert.Equal(duringChildEnumeration ? 0 : 1, scanned.Count);
    }
}
