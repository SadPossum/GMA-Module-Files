namespace Gma.Modules.Files.Application.Handlers;

using Gma.Modules.Files.Application.Commands;
using Gma.Modules.Files.Application.Visibility;
using Gma.Framework.Cqrs;
using Gma.Framework.FileManagement;
using Gma.Framework.Scoping;
using Gma.Framework.Results;

internal sealed class DeleteFileCommandHandler(
    IFileStorage storage,
    IScopeContext scopeContext)
    : ICommandHandler<DeleteFileCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        DeleteFileCommand command,
        CancellationToken cancellationToken)
    {
        Result<Unit> access = FilesAccess.EnsureUserSubject(command.Subject, scopeContext);
        if (access.IsFailure)
        {
            return access;
        }

        if (command.FileId == Guid.Empty)
        {
            return Result.Failure<Unit>(FilesApplicationErrors.FileIdInvalid);
        }

        FileStorageObjectKey key = FilesStorageKeys.For(command.FileId, command.Subject, scopeContext);
        bool deleted = await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            FileStorageObjectKey legacyKey = FilesStorageKeys.LegacyFor(
                command.FileId,
                command.Subject,
                scopeContext);
            deleted = await storage.DeleteAsync(legacyKey, cancellationToken).ConfigureAwait(false);
        }

        return deleted
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>(FilesApplicationErrors.FileNotFound);
    }
}
