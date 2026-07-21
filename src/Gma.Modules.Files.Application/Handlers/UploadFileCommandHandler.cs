namespace Gma.Modules.Files.Application.Handlers;

using Gma.Modules.Files.Application.Commands;
using Gma.Modules.Files.Contracts;
using Gma.Framework.Cqrs;
using Gma.Framework.FileManagement;
using Gma.Framework.Scoping;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.Files.Application.Visibility;

internal sealed class UploadFileCommandHandler(
    IFileStorage storage,
    FileUploadContentGate contentGate,
    IIdGenerator idGenerator,
    IScopeContext scopeContext)
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

        Result<PreparedFileUpload> prepared = await contentGate.PrepareAsync(command, cancellationToken)
            .ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return Result.Failure<FileUploadResponse>(prepared.Error);
        }

        await using PreparedFileUpload upload = prepared.Value;

        Guid fileId = idGenerator.NewId();
        if (fileId == Guid.Empty)
        {
            return Result.Failure<FileUploadResponse>(FilesApplicationErrors.FileIdInvalid);
        }

        FileStorageObjectKey key = FilesStorageKeys.For(fileId, command.Subject, scopeContext);
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["module"] = "files"
        };
        if (upload.Detector is not null)
        {
            metadata["content-type-detector"] = upload.Detector;
        }

        if (upload.Inspector is not null)
        {
            metadata["content-inspector"] = upload.Inspector;
        }

        FileStorageWriteRequest request = new(
            key,
            upload.Content,
            command.ContentLength,
            upload.ContentType,
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
}
