namespace VideoHostingService.Utilities;

/// <summary>
/// Reads <paramref name="prefix"/> first and then continues into <paramref name="rest"/>.
/// Uploads come off <c>IBrowserFile.OpenReadStream</c>, which is forward-only, so the header
/// bytes consumed for magic-byte validation have to be pushed back in front of the stream
/// before it can be handed to the storage client.
/// </summary>
public sealed class PrefixedStream(byte[] prefix, Stream rest) : Stream
{
    private int prefixPosition;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (prefixPosition < prefix.Length)
        {
            var take = Math.Min(buffer.Length, prefix.Length - prefixPosition);
            prefix.AsSpan(prefixPosition, take).CopyTo(buffer);
            prefixPosition += take;
            return take;
        }

        return rest.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (prefixPosition < prefix.Length)
        {
            var take = Math.Min(buffer.Length, prefix.Length - prefixPosition);
            prefix.AsMemory(prefixPosition, take).CopyTo(buffer);
            prefixPosition += take;
            return take;
        }

        return await rest.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            rest.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await rest.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
