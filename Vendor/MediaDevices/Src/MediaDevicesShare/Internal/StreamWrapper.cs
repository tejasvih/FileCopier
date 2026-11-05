namespace MediaDevices.Internal;

internal sealed class StreamWrapper : Stream
{
    private IStream? stream;
    private IDisposable? owner;
    private ulong size;

    // The stream must be uniquely marshalled. The optional owner stays alive until
    // after the native stream has closed, so the next transfer cannot inherit its channel.
    public StreamWrapper(IStream stream, ulong size = 0, IDisposable? owner = null)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.size = size;
        this.owner = owner;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);
            return;
        }

        var nativeStream = Interlocked.Exchange(ref stream, null);
        var resourceOwner = Interlocked.Exchange(ref owner, null);
        try
        {
            ComHelper.ReleaseUnique(nativeStream);
        }
        finally
        {
            try { resourceOwner?.Dispose(); }
            finally { base.Dispose(disposing); }
        }
    }

    private IStream RequireStream()
    {
        ObjectDisposedException.ThrowIf(stream == null, this);
        return stream;
    }

    public override bool CanRead => stream != null;
    public override bool CanSeek => false;
    public override bool CanWrite => stream != null;

    public override void Flush()
    {
        int result = RequireStream().Commit(0);
        MediaDeviceException.ThrowIfComError(result, nameof(IStream), nameof(IStream.Commit));
    }

    public override long Length
    {
        get
        {
            RequireStream();
            return checked((long)size);
        }
    }

    public override long Position
    {
        get => Seek(0, SeekOrigin.Current);
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        var nativeStream = RequireStream();
        if (count == 0) return 0;

        var localBuffer = offset == 0 ? buffer : new byte[count];
        int result = nativeStream.Read(localBuffer, (uint)count, out uint read);
        MediaDeviceException.ThrowIfComError(result, nameof(IStream), nameof(IStream.Read));
        if (read > count)
            throw new IOException("IStream.Read returned more bytes than requested.");

        if (offset != 0)
            Array.Copy(localBuffer, 0, buffer, offset, (int)read);
        return (int)read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        RequireStream();
        throw new NotSupportedException("The device stream does not support seeking.");
    }

    public override void SetLength(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        int result = RequireStream().SetSize(value);
        MediaDeviceException.ThrowIfComError(result, nameof(IStream), nameof(IStream.SetSize));
        size = (ulong)value;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        var nativeStream = RequireStream();
        var completed = 0;
        while (completed < count)
        {
            var remaining = count - completed;
            var start = offset + completed;
            var localBuffer = start == 0 ? buffer : buffer.AsSpan(start, remaining).ToArray();
            int result = nativeStream.Write(localBuffer, (uint)remaining, out uint written);
            MediaDeviceException.ThrowIfComError(result, nameof(IStream), nameof(IStream.Write));
            if (written == 0 || written > remaining)
                throw new IOException("IStream.Write returned an invalid byte count.");
            completed += (int)written;
        }
    }
}
