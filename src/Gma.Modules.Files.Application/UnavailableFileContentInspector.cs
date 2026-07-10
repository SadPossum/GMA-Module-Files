namespace Gma.Modules.Files.Application;

using Gma.Framework.FileManagement;

internal sealed class UnavailableFileContentInspector : IFileContentInspector
{
    public ValueTask<FileContentInspectionResult> InspectAsync(
        FileContentInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(FileContentInspectionResult.Unavailable("none"));
    }
}
