namespace Gma.Modules.Files.Application.Handlers;

using Gma.Modules.Files.Application.Queries;
using Gma.Modules.Files.Application.ReadModels;
using Gma.Modules.Files.Application.Visibility;
using Gma.Framework.Cqrs;
using Gma.Framework.FileManagement;
using Gma.Framework.Scoping;
using Gma.Framework.Results;

internal sealed class GetFileQueryHandler(
    IFileStorage storage,
    IScopeContext scopeContext)
    : IQueryHandler<GetFileQuery, FileDownload>
{
    public async Task<Result<FileDownload>> HandleAsync(
        GetFileQuery query,
        CancellationToken cancellationToken)
    {
        Result<Unit> access = FilesAccess.EnsureUserSubject(query.Subject, scopeContext);
        if (access.IsFailure)
        {
            return Result.Failure<FileDownload>(access.Error);
        }

        if (query.FileId == Guid.Empty)
        {
            return Result.Failure<FileDownload>(FilesApplicationErrors.FileIdInvalid);
        }

        FileStorageObjectKey key = FilesStorageKeys.For(query.FileId, query.Subject, scopeContext);
        FileStorageReadResult? file = await storage.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            FileStorageObjectKey legacyKey = FilesStorageKeys.LegacyFor(
                query.FileId,
                query.Subject,
                scopeContext);
            file = await storage.OpenReadAsync(legacyKey, cancellationToken).ConfigureAwait(false);
        }

        return file is null
            ? Result.Failure<FileDownload>(FilesApplicationErrors.FileNotFound)
            : Result.Success(new FileDownload(file));
    }
}
