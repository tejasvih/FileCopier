using System.Buffers.Binary;
using System.Text;
using FileCopier.Afc;
using Xunit;

namespace FileCopier.Tests;

public sealed class AfcClientTests
{
    private const ulong Status = 0x01, Data = 0x02, ReadDirectory = 0x03, GetFileInfo = 0x0A,
        FileOpen = 0x0D, FileOpenResult = 0x0E, FileRead = 0x0F, FileClose = 0x14;

    [Fact]
    public void ReadDirectoryParsesNamesAndDropsDotEntries()
    {
        var device = new FakeAfcDevice((_, _) => (Data, Strings(".", "..", "100APPLE", "IMG_0001.HEIC")));
        using var client = new AfcClient(device);

        Assert.Equal(["100APPLE", "IMG_0001.HEIC"], client.ReadDirectory("/DCIM"));
        Assert.Equal("/DCIM\0", Encoding.UTF8.GetString(device.Requests.Single().Payload));
    }

    [Fact]
    public void GetFileInfoParsesTypeSizeAndNanosecondTimes()
    {
        var modified = new DateTimeOffset(2026, 7, 4, 7, 26, 7, TimeSpan.Zero);
        var nanoseconds = (modified - DateTimeOffset.UnixEpoch).Ticks * 100;
        var device = new FakeAfcDevice((_, _) => (Data, Strings(
            "st_size", "2653648", "st_ifmt", "S_IFREG", "st_mtime", nanoseconds.ToString(), "st_birthtime", nanoseconds.ToString())));
        using var client = new AfcClient(device);

        var info = client.GetFileInfo("/DCIM/100APPLE/IMG_0001.HEIC");

        Assert.True(info.IsFile);
        Assert.False(info.IsDirectory);
        Assert.Equal(2653648UL, info.Size);
        Assert.Equal(modified.LocalDateTime, info.LastWriteTime);
        Assert.Equal(modified.LocalDateTime, info.CreationTime);
    }

    [Fact]
    public void CopyFileReadsUntilEmptyChunkAndClosesHandle()
    {
        var chunks = new Queue<byte[]>([[1, 2, 3], [4, 5], []]);
        var device = new FakeAfcDevice((operation, payload) => operation switch
        {
            FileOpen => (FileOpenResult, UInt64(42)),
            FileRead => (Data, chunks.Dequeue()),
            FileClose => (Status, UInt64(0)),
            _ => throw new InvalidOperationException(),
        });
        using var client = new AfcClient(device);
        using var destination = new MemoryStream();

        var copied = client.CopyFile("/DCIM/100APPLE/IMG_0001.HEIC", destination);

        Assert.Equal(5UL, copied);
        Assert.Equal([1, 2, 3, 4, 5], destination.ToArray());
        Assert.Equal([FileOpen, FileRead, FileRead, FileRead, FileClose], device.Requests.Select(r => r.Operation));
        Assert.Equal(1UL, BinaryPrimitives.ReadUInt64LittleEndian(device.Requests[0].Payload)); // read-only mode
        Assert.All(device.Requests.Skip(1), request => Assert.Equal(42UL, BinaryPrimitives.ReadUInt64LittleEndian(request.Payload)));
    }

    [Fact]
    public void DestinationFailureClosesHandleAndKeepsOriginalError()
    {
        var device = new FakeAfcDevice((operation, _) => operation switch
        {
            FileOpen => (FileOpenResult, UInt64(7)),
            FileRead => (Data, [1, 2, 3]),
            FileClose => (Status, UInt64(0)),
            _ => throw new InvalidOperationException(),
        });
        using var client = new AfcClient(device);

        var error = Assert.Throws<IOException>(() => client.CopyFile("/DCIM/a.HEIC", new FailingStream()));

        Assert.Equal("disk full", error.Message);
        Assert.Equal(FileClose, device.Requests[^1].Operation);
    }

    [Fact]
    public void StatusErrorThrowsAfcExceptionAndSessionStaysUsable()
    {
        var replies = new Queue<(ulong, byte[])>([(Status, UInt64(AfcException.ObjectNotFound)), (Data, Strings("IMG_0001.HEIC"))]);
        using var client = new AfcClient(new FakeAfcDevice((_, _) => replies.Dequeue()));

        var error = Assert.Throws<AfcException>(() => client.ReadDirectory("/missing"));

        Assert.Equal(AfcException.ObjectNotFound, error.Status);
        Assert.True(error.IsPermanent);
        Assert.Equal(["IMG_0001.HEIC"], client.ReadDirectory("/DCIM/100APPLE"));
    }

    [Fact]
    public void ReplyForAnotherRequestBreaksSessionPermanently()
    {
        var device = new FakeAfcDevice((_, _) => (Data, Strings("stale")), replyNumberOffset: 1);
        using var client = new AfcClient(device);

        Assert.Throws<InvalidDataException>(() => client.ReadDirectory("/DCIM"));
        var requests = device.Requests.Count;
        Assert.Throws<IOException>(() => client.ReadDirectory("/DCIM"));
        Assert.Equal(requests, device.Requests.Count);
    }

    [Fact]
    public void TruncatedReplyFailsInsteadOfReturningPartialData()
    {
        var device = new FakeAfcDevice((_, _) => (Data, Strings("IMG_0001.HEIC")), truncateReplyBy: 4);
        using var client = new AfcClient(device);

        Assert.Throws<EndOfStreamException>(() => client.ReadDirectory("/DCIM"));
        Assert.Throws<IOException>(() => client.ReadDirectory("/DCIM"));
    }

    [Theory]
    [InlineData("IMG_8020", "FullSizeRender.heic", "IMG_E8020.HEIC")]
    [InlineData("IMG_8823", "FullSizeRender.mov", "IMG_E8823.MOV")]
    [InlineData("IMG_8020", "Adjustments.plist", "IMG_8020.AAE")]
    [InlineData("IMG_8020", "PenultimateFullSizeRender.heic", null)]
    [InlineData("IMG_8649", "IMG_8649.JPG", null)]
    [InlineData("IMG_8649", "AsyncRenderPreview.JPG", null)]
    [InlineData("IMG_8649", "FullSizeRender.", null)]
    [InlineData("PIC_0001", "FullSizeRender.jpg", "PIC_0001_E.JPG")]
    public void EditFilesUseTheNamesWindowsExportsThemUnder(string item, string file, string? expected)
    {
        Assert.Equal(expected, AfcSource.GetEditBackupName(item, file));
    }

    [Theory]
    [InlineData("IMG_6388.AAE", "IMG_O6388.AAE")]
    [InlineData("PIC_0001.AAE", "PIC_0001_O.AAE")]
    public void EditedItemsCameraRollAdjustmentsUseTheOriginalAdjustmentsName(string file, string expected)
    {
        Assert.Equal(expected, AfcSource.GetOriginalAdjustmentsName(file));
    }

    private static byte[] Strings(params string[] values) =>
        Encoding.UTF8.GetBytes(string.Concat(values.Select(value => value + "\0")));

    private static byte[] UInt64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }

    private sealed class FailingStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("disk full");
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");
    }

    /// <summary>Answers each complete AFC request packet with the handler's reply.</summary>
    private sealed class FakeAfcDevice(
        Func<ulong, byte[], (ulong Operation, byte[] Payload)> handler,
        ulong replyNumberOffset = 0,
        int truncateReplyBy = 0) : Stream
    {
        private readonly List<byte> pending = [];
        private readonly Queue<byte> replies = new();

        public List<(ulong Operation, byte[] Payload)> Requests { get; } = [];

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            pending.AddRange(buffer.ToArray());
            while (pending.Count >= 40)
            {
                var header = pending.Take(40).ToArray();
                Assert.Equal("CFA6LPAA", Encoding.ASCII.GetString(header, 0, 8));
                var length = (int)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8));
                if (pending.Count < length) return;

                var number = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24));
                var operation = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32));
                var payload = pending.Skip(40).Take(length - 40).ToArray();
                pending.RemoveRange(0, length);
                Requests.Add((operation, payload));

                var (replyOperation, replyPayload) = handler(operation, payload);
                var reply = new byte[40 + replyPayload.Length];
                "CFA6LPAA"u8.CopyTo(reply);
                BinaryPrimitives.WriteUInt64LittleEndian(reply.AsSpan(8), (ulong)reply.Length);
                BinaryPrimitives.WriteUInt64LittleEndian(reply.AsSpan(16), 40);
                BinaryPrimitives.WriteUInt64LittleEndian(reply.AsSpan(24), number + replyNumberOffset);
                BinaryPrimitives.WriteUInt64LittleEndian(reply.AsSpan(32), replyOperation);
                replyPayload.CopyTo(reply, 40);
                foreach (var value in reply.AsSpan(0, reply.Length - truncateReplyBy)) replies.Enqueue(value);
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = 0;
            while (read < buffer.Length && replies.TryDequeue(out var value)) buffer[read++] = value;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
