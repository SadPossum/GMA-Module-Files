namespace Gma.Modules.Files.Application.Handlers;

using Gma.Modules.Files.Application.Commands;
using Gma.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using Gma.Framework.Cqrs;
using Gma.Framework.FileManagement;
using Gma.Framework.Scoping;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.Files.Application.Visibility;

internal sealed class UploadFileCommandHandler(
    IFileStorage storage,
    IFileContentInspector contentInspector,
    IIdGenerator idGenerator,
    IScopeContext scopeContext,
    IOptions<FileManagementOptions> fileManagementOptions)
    : ICommandHandler<UploadFileCommand, FileUploadResponse>
{
    public async Task<Result<FileUploadResponse>> HandleAsync(
        UploadFileCommand command,
        CancellationToken cancellationToken)
    {
        Result<Unit> access = FilesAccess.EnsureUserSubject(command.Subject, scopeContext);
        if (access.IsFailure)
        {
            return Result.Failure<FileUploadResponse>(access.Error);
        }

        if (command.Content is null || !command.Content.CanRead)
        {
            return Result.Failure<FileUploadResponse>(FilesApplicationErrors.FileRequired);
        }

        if (command.ContentLength <= 0)
        {
            return Result.Failure<FileUploadResponse>(FilesApplicationErrors.FileEmpty);
        }

        FileManagementOptions options = fileManagementOptions.Value;
        if (command.ContentLength > options.MaximumObjectBytes)
        {
            return Result.Failure<FileUploadResponse>(FilesApplicationErrors.FileTooLarge);
        }

        string contentType = FileStorageMetadata.ContentTypeOrDefault(command.ContentType);
        if (options.AllowedContentTypes.Length > 0 &&
            !options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<FileUploadResponse>(FilesApplicationErrors.ContentTypeNotAllowed);
        }

        Guid fileId = idGenerator.NewId();
        if (fileId == Guid.Empty)
        {
            return Result.Failure<FileUploadResponse>(FilesApplicationErrors.FileIdInvalid);
        }

        FileStream? inspectedContent = null;
        Stream contentToStore = command.Content;
        string? inspector = null;
        if (options.RequireContentInspection)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"gma-upload-{Path.GetRandomFileName()}.tmp");
            inspectedContent = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            long copiedLength = await CopyBoundedAsync(
                    command.Content,
                    inspectedContent,
                    options.MaximumObjectBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            if (copiedLength > options.MaximumObjectBytes)
            {
                await inspectedContent.DisposeAsync().ConfigureAwait(false);
                return Result.Failure<FileUploadResponse>(FilesApplicationErrors.FileTooLarge);
            }

            if (copiedLength != command.ContentLength)
            {
                await inspectedContent.DisposeAsync().ConfigureAwait(false);
                return Result.Failure<FileUploadResponse>(FilesApplicationErrors.ContentLengthMismatch);
            }

            inspectedContent.Position = 0;
            FileContentInspectionResult inspection;
            try
            {
                inspection = await contentInspector.InspectAsync(
                        new FileContentInspectionRequest(
                            inspectedContent,
                            inspectedContent.Length,
                            contentType,
                            command.FileName),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await inspectedContent.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            if (inspection.Status != FileContentInspectionStatus.Clean)
            {
                await inspectedContent.DisposeAsync().ConfigureAwait(false);
                return Result.Failure<FileUploadResponse>(
                    inspection.Status == FileContentInspectionStatus.Rejected
                        ? FilesApplicationErrors.ContentRejected
                        : FilesApplicationErrors.ContentInspectionRequired);
            }

            inspectedContent.Position = 0;
            contentToStore = inspectedContent;
            inspector = inspection.Inspector;
        }

        FileStorageObjectKey key = FilesStorageKeys.For(fileId, command.Subject, scopeContext);
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["module"] = "files"
        };
        if (inspector is not null)
        {
            metadata["content-inspector"] = inspector;
        }

        try
        {
            FileStorageWriteRequest request = new(
                key,
                contentToStore,
                command.ContentLength,
                contentType,
                command.FileName,
                metadata);

            FileStorageObjectProperties stored = await storage.PutAsync(request, cancellationToken).ConfigureAwait(false);
            FileUploadResponse response = new(
                fileId,
                stored.ContentType,
                stored.ContentLength,
                stored.FileName,
                $"/api/files/{fileId:D}");

            return Result.Success(response);
        }
        finally
        {
            if (inspectedContent is not null)
            {
                await inspectedContent.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

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
