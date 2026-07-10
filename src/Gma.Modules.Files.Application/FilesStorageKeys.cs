namespace Gma.Modules.Files.Application;

using System.Security.Cryptography;
using System.Text;
using Gma.Framework.AccessControl;
using Gma.Framework.FileManagement;
using Gma.Framework.Scoping;

internal static class FilesStorageKeys
{
    public static FileStorageObjectKey For(
        Guid fileId,
        AccessSubject subject,
        IScopeContext scopeContext)
    {
        if (fileId == Guid.Empty)
        {
            throw new ArgumentException("File id cannot be empty.", nameof(fileId));
        }

        string scopeSegment = ScopeSegment(scopeContext);
        string subjectSegment = SubjectSegment(subject);
        return new FileStorageObjectKey($"files/{scopeSegment}/{subjectSegment}/{fileId:N}");
    }

    private static string ScopeSegment(IScopeContext scopeContext)
    {
        if (!scopeContext.IsEnabled)
        {
            return "global";
        }

        if (string.IsNullOrWhiteSpace(scopeContext.ScopeId))
        {
            throw new InvalidOperationException("Scope id is required when scoping is enabled.");
        }

        return $"scope-{HashSegment(scopeContext.ScopeId)}";
    }

    private static string SubjectSegment(AccessSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        string kind = subject.Kind switch
        {
            AccessSubjectKind.User => "user",
            AccessSubjectKind.AdminActor => "admin",
            AccessSubjectKind.Service => "service",
            AccessSubjectKind.System => "system",
            _ => throw new ArgumentException("File subject kind is not supported.", nameof(subject))
        };

        return $"{kind}-{HashSegment(subject.Id)}";
    }

    private static string HashSegment(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }
}
