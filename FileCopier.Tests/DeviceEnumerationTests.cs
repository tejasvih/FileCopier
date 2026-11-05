using MediaDevices;
using MediaDevices.Internal;
using Xunit;

namespace FileCopier.Tests;

public sealed class DeviceEnumerationTests
{
    private const int EFail = unchecked((int)0x80004005);

    [Fact]
    public void EnumeratesAll128ObjectsAcrossFourPages()
    {
        var nextId = 0;
        var calls = 0;
        var enumerator = new StubObjectEnumerator((uint requested, string[] ids, ref uint fetched) =>
        {
            Assert.Equal(0u, fetched);
            calls++;
            fetched = nextId < 128 ? requested : 0;
            for (var index = 0; index < fetched; index++) ids[index] = $"object-{++nextId}";
            return fetched == 0 ? 1 : 0;
        });

        var objects = CheckedObjectEnumerator.Enumerate(0, enumerator, id => id).ToArray();

        Assert.Equal(128, objects.Length);
        Assert.Equal(128, objects.Distinct().Count());
        Assert.Equal("object-1", objects[0]);
        Assert.Equal("object-128", objects[^1]);
        Assert.Equal(5, calls);
    }

    [Fact]
    public void SecondPageFailurePropagatesInsteadOfReportingPartialSuccess()
    {
        var calls = 0;
        var enumerator = new StubObjectEnumerator((uint requested, string[] ids, ref uint fetched) =>
        {
            Assert.Equal(0u, fetched);
            if (++calls == 2) return EFail;
            fetched = requested;
            for (var index = 0; index < fetched; index++) ids[index] = $"object-{index}";
            return 0;
        });
        using var objects = CheckedObjectEnumerator.Enumerate(0, enumerator, id => id).GetEnumerator();
        for (var index = 0; index < 32; index++) Assert.True(objects.MoveNext());

        var error = Assert.Throws<MediaDeviceException>(() => objects.MoveNext());

        Assert.Contains("0x80004005", error.Message);
        Assert.Contains("IEnumPortableDeviceObjectIDs.Next", error.Message);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void SFalseRetainsFinalPartialPageWithoutAnotherRead()
    {
        var calls = 0;
        var enumerator = new StubObjectEnumerator((uint _, string[] ids, ref uint fetched) =>
        {
            Assert.Equal(1, ++calls);
            ids[0] = "first";
            ids[1] = "second";
            fetched = 2;
            return 1;
        });

        Assert.Equal(["first", "second"], CheckedObjectEnumerator.Enumerate(0, enumerator, id => id));
    }

    [Fact]
    public void EmptySFalsePageEndsEnumeration()
    {
        var enumerator = new StubObjectEnumerator((uint _, string[] _, ref uint fetched) =>
        {
            fetched = 0;
            return 1;
        });

        Assert.Empty(CheckedObjectEnumerator.Enumerate(0, enumerator, id => id));
    }

    [Fact]
    public void EmptySOkPageCannotSilentlyTruncateEnumeration()
    {
        var enumerator = new StubObjectEnumerator((uint _, string[] _, ref uint fetched) =>
        {
            fetched = 0;
            return 0;
        });

        Assert.Throws<IOException>(() =>
            CheckedObjectEnumerator.Enumerate(0, enumerator, id => id).ToArray());
    }

    [Fact]
    public void InitialEnumObjectsFailureIsNotTreatedAsEmptyFolder()
    {
        var enumerator = new StubObjectEnumerator((uint _, string[] _, ref uint _) =>
            throw new InvalidOperationException("Next must not run after EnumObjects fails."));

        var error = Assert.Throws<MediaDeviceException>(() =>
            CheckedObjectEnumerator.Enumerate(EFail, enumerator, id => id).ToArray());

        Assert.Contains("IPortableDeviceContent.EnumObjects", error.Message);
        Assert.Contains("0x80004005", error.Message);
    }

    [Fact]
    public void NullEnumeratorIsNotTreatedAsEmptyFolder()
    {
        Assert.Throws<IOException>(() =>
            CheckedObjectEnumerator.Enumerate(0, null, id => id).ToArray());
    }

    [Fact]
    public void FirstPageFailurePropagates()
    {
        var enumerator = new StubObjectEnumerator((uint _, string[] _, ref uint _) => EFail);

        Assert.Throws<MediaDeviceException>(() =>
            CheckedObjectEnumerator.Enumerate(0, enumerator, id => id).ToArray());
    }

    [Fact]
    public void FailedPageDoesNotExposeObjectsEvenWhenDriverReturnsData()
    {
        var enumerator = new StubObjectEnumerator((uint _, string[] ids, ref uint fetched) =>
        {
            ids[0] = "incomplete";
            fetched = 1;
            return EFail;
        });

        Assert.Throws<MediaDeviceException>(() => CheckedObjectEnumerator.Enumerate<string>(0, enumerator,
            id => throw new InvalidOperationException("Failed page data must not be read.")).ToArray());
    }

    [Fact]
    public void MissingObjectMetadataPropagatesInsteadOfSilentlySkippingObject()
    {
        var failure = new FileNotFoundException("Object metadata vanished during enumeration.");
        var enumerator = new StubObjectEnumerator((uint _, string[] ids, ref uint fetched) =>
        {
            ids[0] = "readable";
            ids[1] = "missing";
            ids[2] = "later";
            fetched = 3;
            return 1;
        });
        using var objects = CheckedObjectEnumerator.Enumerate(0, enumerator,
            id => id == "missing" ? throw failure : id).GetEnumerator();
        Assert.True(objects.MoveNext());
        Assert.Equal("readable", objects.Current);

        Assert.Same(failure, Assert.Throws<FileNotFoundException>(() => objects.MoveNext()));
    }

    [Fact]
    public void InvalidFetchedCountFailsBeforeReadingArray()
    {
        var enumerator = new StubObjectEnumerator((uint requested, string[] _, ref uint fetched) =>
        {
            fetched = requested + 1;
            return 0;
        });

        Assert.Throws<IOException>(() =>
            CheckedObjectEnumerator.Enumerate(0, enumerator, id => id).ToArray());
    }

    [Fact]
    public void EmptyObjectIdIsNotSilentlySkipped()
    {
        var enumerator = new StubObjectEnumerator((uint _, string[] ids, ref uint fetched) =>
        {
            ids[0] = "";
            fetched = 1;
            return 1;
        });

        Assert.Throws<IOException>(() =>
            CheckedObjectEnumerator.Enumerate(0, enumerator, id => id).ToArray());
    }

    [Fact]
    public void UnwrittenIdCannotReuseDataFromPreviousPage()
    {
        var calls = 0;
        var enumerator = new StubObjectEnumerator((uint _, string[] ids, ref uint fetched) =>
        {
            fetched = 2;
            ids[0] = ++calls == 1 ? "previous-first" : "new";
            if (calls == 1) ids[1] = "previous-second";
            return calls == 1 ? 0 : 1;
        });
        using var objects = CheckedObjectEnumerator.Enumerate(0, enumerator, id => id).GetEnumerator();
        Assert.True(objects.MoveNext());
        Assert.True(objects.MoveNext());
        Assert.True(objects.MoveNext());
        Assert.Equal("new", objects.Current);

        Assert.Throws<IOException>(() => objects.MoveNext());
    }

    private delegate int ReadPage(uint requested, string[] ids, ref uint fetched);

    private sealed class StubObjectEnumerator(ReadPage readPage) : IEnumPortableDeviceObjectIDs
    {
        public int Next(uint cObjects, string[] pObjIDs, ref uint pcFetched) =>
            readPage(cObjects, pObjIDs, ref pcFetched);

        public int Skip(uint cObjects) => throw new NotSupportedException();
        public int Reset() => throw new NotSupportedException();
        public int Clone(out IEnumPortableDeviceObjectIDs ppenum) => throw new NotSupportedException();
        public int Cancel() => throw new NotSupportedException();
    }
}
