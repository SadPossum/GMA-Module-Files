namespace Gma.Modules.Files.Application;

using Gma.Framework.FileManagement;

internal sealed class UnavailableFileContentTypeDetector :
    IFileContentTypeDetector,
    IFileContentTypeDetectorReadiness
{
    public ValueTask<FileContentTypeDetectionResult> DetectAsync(
        FileContentTypeDetectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(FileContentTypeDetectionResult.Unavailable("none"));
    }

    public ValueTask<FileContentCapabilityReadiness> CheckReadinessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(FileContentCapabilityReadiness.Unavailable("none"));
    }
}
