namespace Gma.Modules.Files.Application;

internal sealed class BorrowedReadOnlyStream(Stream source) : Stream
{
    private int disposed;

    internal Stream Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    public override bool CanRead => Volatile.Read(ref this.disposed) == 0 && this.Source.CanRead;
    public override bool CanSeek => Volatile.Read(ref this.disposed) == 0 && this.Source.CanSeek;
    public override bool CanWrite => false;
    public override long Length => this.GetSource().Length;

    public override long Position
    {
        get => this.GetSource().Position;
        set => this.GetSource().Position = value;
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) =>
        this.GetSource().Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => this.GetSource().Read(buffer);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        this.GetSource().ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        this.GetSource().ReadAsync(buffer, cancellationToken);

    public override int ReadByte() => this.GetSource().ReadByte();

    public override long Seek(long offset, SeekOrigin origin) => this.GetSource().Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException("The borrowed stream is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("The borrowed stream is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref this.disposed, 1) == 0)
        {
            base.Dispose(disposing);
        }
    }

    private Stream GetSource()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
        return this.Source;
    }
}
