namespace Gma.Modules.Files.Application.Visibility;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Scoping;
using Gma.Framework.Results;

internal static class FilesAccess
{
    public static Result<Unit> EnsureUserSubject(
        AccessSubject? subject,
        IScopeContext scopeContext)
    {
        if (subject is null || subject.Kind != AccessSubjectKind.User)
        {
            return Result.Failure<Unit>(FilesApplicationErrors.AccessDenied);
        }

        if (!scopeContext.IsEnabled)
        {
            return Result.Success(Unit.Value);
        }

        if (string.IsNullOrWhiteSpace(scopeContext.ScopeId))
        {
            return Result.Failure<Unit>(FilesApplicationErrors.ScopeRequired);
        }

        return Result.Success(Unit.Value);
    }
}
