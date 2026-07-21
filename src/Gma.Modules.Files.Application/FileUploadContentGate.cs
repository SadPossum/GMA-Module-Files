namespace Gma.Modules.Files.Application;

using Gma.Framework.FileManagement;
using Gma.Framework.Results;
using Gma.Modules.Files.Application.Commands;
using Microsoft.Extensions.Options;

internal sealed class FileUploadContentGate(
    IFileContentTypeDetector contentTypeDetector,
    IFileContentInspector contentInspector,
    IOptions<FileManagementOptions> fileManagementOptions,
    IOptions<FilesUploadOptions> uploadOptions)
{
    public async Task<Result<PreparedFileUpload>> PrepareAsync(
        UploadFileCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Content is null || !command.Content.CanRead)
        {
            return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.FileRequired);
        }

        if (command.ContentLength <= 0)
        {
            return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.FileEmpty);
        }

        FileManagementOptions storagePolicy = fileManagementOptions.Value;
        if (command.ContentLength > storagePolicy.MaximumObjectBytes)
        {
            return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.FileTooLarge);
        }

        FilesUploadOptions policy = uploadOptions.Value;
        string contentType = FileStorageMetadata.ContentTypeOrDefault(command.ContentType);
        if (!policy.RequireTrustedContentType && !IsAllowed(contentType, storagePolicy))
        {
            return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.ContentTypeNotAllowed);
        }

        if (!policy.RequireTrustedContentType && !policy.RequireContentInspection)
        {
            return Result.Success(PreparedFileUpload.Borrowed(command.Content, contentType));
        }

        string tempPath = Path.Combine(Path.GetTempPath(), $"gma-upload-{Path.GetRandomFileName()}.tmp");
        FileStream quarantine = new(
            tempPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);

        try
        {
            long copiedLength = await CopyBoundedAsync(
                    command.Content,
                    quarantine,
                    storagePolicy.MaximumObjectBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            if (copiedLength > storagePolicy.MaximumObjectBytes)
            {
                await quarantine.DisposeAsync().ConfigureAwait(false);
                return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.FileTooLarge);
            }

            if (copiedLength != command.ContentLength)
            {
                await quarantine.DisposeAsync().ConfigureAwait(false);
                return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.ContentLengthMismatch);
            }

            string? detector = null;
            if (policy.RequireTrustedContentType)
            {
                quarantine.Position = 0;
                FileContentTypeDetectionResult detection = await contentTypeDetector.DetectAsync(
                        new FileContentTypeDetectionRequest(
                            quarantine,
                            quarantine.Length,
                            command.FileName),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (detection.Status == FileContentTypeDetectionStatus.Unavailable)
                {
                    await quarantine.DisposeAsync().ConfigureAwait(false);
                    return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.ContentTypeDetectionRequired);
                }

                if (detection.Status != FileContentTypeDetectionStatus.Detected || detection.ContentType is null)
                {
                    await quarantine.DisposeAsync().ConfigureAwait(false);
                    return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.ContentTypeUnrecognized);
                }

                contentType = detection.ContentType;
                detector = detection.Detector;
                if (!IsAllowed(contentType, storagePolicy))
                {
                    await quarantine.DisposeAsync().ConfigureAwait(false);
                    return Result.Failure<PreparedFileUpload>(FilesApplicationErrors.ContentTypeNotAllowed);
                }
            }

            string? inspector = null;
            if (policy.RequireContentInspection)
            {
                quarantine.Position = 0;
                FileContentInspectionResult inspection = await contentInspector.InspectAsync(
                        new FileContentInspectionRequest(
                            quarantine,
                            quarantine.Length,
                            contentType,
                            command.FileName),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (inspection.Status != FileContentInspectionStatus.Clean)
                {
                    await quarantine.DisposeAsync().ConfigureAwait(false);
                    return Result.Failure<PreparedFileUpload>(
                        inspection.Status == FileContentInspectionStatus.Rejected
                            ? FilesApplicationErrors.ContentRejected
                            : FilesApplicationErrors.ContentInspectionRequired);
                }

                inspector = inspection.Inspector;
            }

            quarantine.Position = 0;
            return Result.Success(PreparedFileUpload.Quarantined(
                quarantine,
                contentType,
                detector,
                inspector));
        }
        catch
        {
            await quarantine.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsAllowed(string contentType, FileManagementOptions options) =>
        options.AllowedContentTypes.Length == 0 ||
        options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    private static async Task<long> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81_920];
        long total = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return total;
            }

            total += read;
            if (total > maximumBytes)
            {
                return total;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
