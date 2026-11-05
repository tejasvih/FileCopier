using MediaDevices;
using MediaDevices.Interfaces;
using MediaDevices.Internal;
using Xunit;

namespace FileCopier.Tests;

public sealed class StreamWrapperTests
{
    private const int EFail = unchecked((int)0x80004005);

    [Fact]
    public void DisposeClosesStreamBeforeOwnerExactlyOnce()
    {
        var order = new List<string>();
        var native = new ManagedNativeStream([1, 2, 3]) { OnDispose = () => order.Add("stream") };
        var owner = new DisposeProbe(() => order.Add("owner"));
        var wrapper = new StreamWrapper(native, 3, owner);

        wrapper.Dispose();
        wrapper.Dispose();

        Assert.Equal(["stream", "owner"], order);
        Assert.Equal(1, native.DisposeCount);
        Assert.Equal(1, owner.DisposeCount);
        Assert.False(wrapper.CanRead);
        Assert.False(wrapper.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => wrapper.Read(new byte[1], 0, 1));
        Assert.Throws<ObjectDisposedException>(() => wrapper.Flush());
    }

    [Fact]
    public void OwnerStillClosesWhenStreamDisposeFails()
    {
        var native = new ManagedNativeStream([]) { DisposeFailure = new IOException("Close failed.") };
        var owner = new DisposeProbe(() => { });
        var wrapper = new StreamWrapper(native, owner: owner);

        Assert.Throws<IOException>(() => wrapper.Dispose());
        wrapper.Dispose();

        Assert.Equal(1, native.DisposeCount);
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public void SuccessiveStreamsReturnIndependentPayloadsAndLeaveDestinationOpen()
    {
        var payloads = new[] { new byte[] { 1 }, new byte[] { 2, 3, 4 }, new byte[] { 5, 6 } };
        foreach (var payload in payloads)
        {
            var native = new ManagedNativeStream(payload);
            using var output = new MemoryStream();
            using (var wrapper = new StreamWrapper(native, (ulong)payload.Length))
                wrapper.CopyTo(output, 2);

            Assert.Equal(payload, output.ToArray());
            Assert.True(output.CanWrite);
            Assert.Equal(1, native.DisposeCount);
        }
    }

    [Fact]
    public void ReadSupportsOffsetWithoutChangingUnwrittenBytes()
    {
        using var wrapper = new StreamWrapper(new ManagedNativeStream([1, 2, 3]), 3);
        var buffer = Enumerable.Repeat((byte)9, 8).ToArray();

        Assert.Equal(3, wrapper.Read(buffer, 2, 4));
        Assert.Equal(new byte[] { 9, 9, 1, 2, 3, 9, 9, 9 }, buffer);
    }

    [Fact]
    public void ReadRejectsNativeCountGreaterThanRequested()
    {
        using var wrapper = new StreamWrapper(new ManagedNativeStream([]) { ReportedRead = uint.MaxValue });

        Assert.Throws<IOException>(() => wrapper.Read(new byte[8], 0, 8));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(4, 2)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void InvalidBufferArgumentsNeverReachNativeStream(int offset, int count)
    {
        var native = new ManagedNativeStream([]);
        using var wrapper = new StreamWrapper(native);

        Assert.ThrowsAny<ArgumentException>(() => wrapper.Read(new byte[4], offset, count));
        Assert.ThrowsAny<ArgumentException>(() => wrapper.Write(new byte[4], offset, count));
        Assert.Equal(0, native.ReadCalls);
        Assert.Equal(0, native.WriteCalls);
    }

    [Fact]
    public void NullBuffersAndZeroLengthOperationsAvoidNativeCalls()
    {
        var native = new ManagedNativeStream([]);
        using var wrapper = new StreamWrapper(native);
        Assert.Throws<ArgumentNullException>(() => wrapper.Read(null!, 0, 0));
        Assert.Throws<ArgumentNullException>(() => wrapper.Write(null!, 0, 0));
        Assert.Equal(0, wrapper.Read([], 0, 0));
        wrapper.Write([], 0, 0);

        Assert.Equal(0, native.ReadCalls);
        Assert.Equal(0, native.WriteCalls);
    }

    [Fact]
    public void FailedReadPropagatesHResult()
    {
        using var wrapper = new StreamWrapper(new ManagedNativeStream([]) { ReadResult = EFail });

        var error = Assert.Throws<MediaDeviceException>(() => wrapper.Read(new byte[1], 0, 1));
        Assert.Contains("0x80004005", error.Message);
    }

    [Fact]
    public void PartialWritesContinueUntilAllRequestedBytesAreWritten()
    {
        var native = new ManagedNativeStream([]) { MaximumWrite = 2 };
        using var wrapper = new StreamWrapper(native);

        wrapper.Write([9, 1, 2, 3, 4, 5, 9], 1, 5);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, native.Written.ToArray());
        Assert.Equal(3, native.WriteCalls);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void InvalidNativeWriteCountFails(uint written)
    {
        using var wrapper = new StreamWrapper(new ManagedNativeStream([]) { ReportedWrite = written });

        Assert.Throws<IOException>(() => wrapper.Write([1], 0, 1));
    }

    [Fact]
    public void WriteFlushAndSetLengthPropagateHResults()
    {
        var native = new ManagedNativeStream([])
        {
            WriteResult = EFail, CommitResult = EFail, SetSizeResult = EFail
        };
        using var wrapper = new StreamWrapper(native);

        Assert.Throws<MediaDeviceException>(() => wrapper.Write([1], 0, 1));
        Assert.Throws<MediaDeviceException>(() => wrapper.Flush());
        Assert.Throws<MediaDeviceException>(() => wrapper.SetLength(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wrapper.SetLength(-1));
    }

    private sealed class DisposeProbe(Action onDispose) : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() { DisposeCount++; onDispose(); }
    }

    private sealed class ManagedNativeStream(byte[] payload) : IStream, IDisposable
    {
        private readonly MemoryStream input = new(payload);
        public MemoryStream Written { get; } = new();
        public uint? ReportedRead { get; init; }
        public uint? ReportedWrite { get; init; }
        public uint MaximumWrite { get; init; } = uint.MaxValue;
        public int ReadResult { get; init; }
        public int WriteResult { get; init; }
        public int CommitResult { get; init; }
        public int SetSizeResult { get; init; }
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public int DisposeCount { get; private set; }
        public Action? OnDispose { get; init; }
        public Exception? DisposeFailure { get; init; }

        public int Read(byte[] pv, uint cb, out uint read)
        {
            ReadCalls++;
            read = ReportedRead ?? (uint)input.Read(pv, 0, checked((int)cb));
            return ReadResult;
        }

        public int Write(byte[] pv, uint cb, out uint written)
        {
            WriteCalls++;
            written = ReportedWrite ?? Math.Min(cb, MaximumWrite);
            if (!ReportedWrite.HasValue) Written.Write(pv, 0, checked((int)written));
            return WriteResult;
        }

        public int Commit(int grfCommitFlags) => CommitResult;
        public int SetSize(long libNewSize) => SetSizeResult;
        public int Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition) => throw new NotSupportedException();
        public int CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotSupportedException();
        public void Revert() => throw new NotSupportedException();
        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();
        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

        public void Dispose()
        {
            DisposeCount++;
            OnDispose?.Invoke();
            input.Dispose();
            if (DisposeFailure != null) throw DisposeFailure;
        }
    }
}
