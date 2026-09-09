using VideoHostingService.Models;

namespace VideoHostingService.Utilities;

/// <summary>
/// Reports how much of <paramref name="inner"/> has been read. Wrapping the source stream keeps
/// upload progress independent of the storage client's own progress API.
/// </summary>
public sealed class ProgressStream(
    Stream inner,
    long totalBytes,
    IProgress<UploadProgress> progress,
    TimeSpan? reportInterval = null) : Stream
{
    private readonly TimeSpan interval = reportInterval ?? TimeSpan.FromMilliseconds(200);
    private long bytesRead;
    private long lastReportedTicks;
    private int lastReportedPercentage = -1;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => totalBytes;

    public override long Position
    {
        get => bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Advance(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Advance(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Advance(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Advance(int read)
    {
        if (read <= 0)
        {
            // End of stream: always report the final state.
            Report(force: true);
            return;
        }

        bytesRead += read;
        Report(force: false);
    }

    private void Report(bool force)
    {
        var report = new UploadProgress(bytesRead, totalBytes);

        if (!force)
        {
            // Throttle: a 2 GB upload otherwise fires a render per network buffer.
            var now = Environment.TickCount64;
            if (report.Percentage == lastReportedPercentage ||
                now - lastReportedTicks < interval.TotalMilliseconds)
            {
                return;
            }

            lastReportedTicks = now;
        }

        lastReportedPercentage = report.Percentage;
        progress.Report(report);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
