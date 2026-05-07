using System.IO;

namespace Contoso.BffApi.Endpoints;

/// <summary>
/// Stream wrapper that enforces a hard byte cap on reads. Once the cap
/// is exceeded the next read throws <see cref="InvalidOperationException"/>
/// — preventing a hostile or buggy upstream from forcing an unbounded
/// allocation in callers that don't otherwise bound their per-read size
/// (e.g., <see cref="StreamReader.ReadLineAsync"/>, which buffers a
/// whole line before returning and has no built-in cap).
/// </summary>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private long _bytesRead;

    public BoundedReadStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _maxBytes = maxBytes;
    }

    public long BytesRead => _bytesRead;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Account(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        Account(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        Account(read);
        return read;
    }

    private void Account(int read)
    {
        if (read <= 0)
        {
            return;
        }
        _bytesRead += read;
        if (_bytesRead > _maxBytes)
        {
            throw new InvalidOperationException(
                $"Upstream stream exceeded the maximum allowed size of {_maxBytes} bytes.");
        }
    }

    public override void Flush() { /* read-only */ }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
