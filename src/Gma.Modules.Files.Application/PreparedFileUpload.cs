namespace Gma.Modules.Files.Application;

internal sealed class PreparedFileUpload(
    Stream content,
    string contentType,
    string? detector,
    string? inspector,
    bool ownsContent) : IAsyncDisposable
{
    private int disposed;

    public Stream Content { get; } = content;
    public string ContentType { get; } = contentType;
    public string? Detector { get; } = detector;
    public string? Inspector { get; } = inspector;

    public static PreparedFileUpload Borrowed(Stream content, string contentType) =>
        new(content, contentType, detector: null, inspector: null, ownsContent: false);

    public static PreparedFileUpload Quarantined(
        Stream content,
        string contentType,
        string? detector,
        string? inspector) =>
        new(content, contentType, detector, inspector, ownsContent: true);

    public ValueTask DisposeAsync()
    {
        if (!ownsContent || Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return this.Content.DisposeAsync();
    }
}
