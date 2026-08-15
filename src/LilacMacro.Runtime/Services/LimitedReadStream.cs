namespace LilacMacro.Runtime.Services;

internal sealed class LimitedReadStream(Stream inner, long length, bool ownsInner) : Stream
{
    private long _remaining = length >= 0
        ? length
        : throw new ArgumentOutOfRangeException(nameof(length));

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => length - _remaining;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
        _remaining -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(
            buffer[..(int)Math.Min(buffer.Length, _remaining)],
            cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && ownsInner) inner.Dispose();
        base.Dispose(disposing);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
