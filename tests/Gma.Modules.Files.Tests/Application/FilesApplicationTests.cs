namespace Gma.Modules.Files.Tests;

using System.Text;
using Gma.Framework.AccessControl;
using Gma.Framework.Api.Modules;
using Gma.Framework.Cqrs;
using Gma.Framework.Cqrs.Infrastructure;
using Gma.Framework.FileManagement;
using Gma.Framework.FileManagement.LocalStorage;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Scoping;
using Gma.Framework.Scoping.Infrastructure;
using Gma.Modules.Files.Api;
using Gma.Modules.Files.Application;
using Gma.Modules.Files.Application.Commands;
using Gma.Modules.Files.Application.Queries;
using Gma.Modules.Files.Application.ReadModels;
using Gma.Modules.Files.Application.Visibility;
using Gma.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[Trait("Category", "Unit")]
public sealed class FilesApplicationTests
{
    [Fact]
    public void Storage_keys_use_versioned_full_digests_and_preserve_legacy_shape()
    {
        Guid fileId = Guid.Parse("019f4a7c-1000-7000-8000-000000000001");
        AccessSubject subject = AccessSubject.User("user-a");
        TestScopeContext scope = new(IsEnabled: true, ScopeId: "scope-a");

        string[] currentSegments = FilesStorageKeys.For(fileId, subject, scope).Value.Split('/');
        string[] legacySegments = FilesStorageKeys.LegacyFor(fileId, subject, scope).Value.Split('/');

        Assert.Equal(["files", "v2"], currentSegments[..2]);
        Assert.StartsWith("scope-", currentSegments[2], StringComparison.Ordinal);
        Assert.Equal(64, currentSegments[2]["scope-".Length..].Length);
        Assert.StartsWith("user-", currentSegments[3], StringComparison.Ordinal);
        Assert.Equal(64, currentSegments[3]["user-".Length..].Length);
        Assert.Equal(fileId.ToString("N"), currentSegments[4]);

        Assert.Equal("files", legacySegments[0]);
        Assert.Equal(16, legacySegments[1]["scope-".Length..].Length);
        Assert.Equal(16, legacySegments[2]["user-".Length..].Length);
        Assert.Equal(fileId.ToString("N"), legacySegments[3]);
    }

    [Fact]
    public void Access_requires_a_user_and_an_active_scope_when_scoping_is_enabled()
    {
        Result<Unit> missingScope = FilesAccess.EnsureUserSubject(
            AccessSubject.User("user-a"),
            new TestScopeContext(IsEnabled: true, ScopeId: null));
        Result<Unit> serviceSubject = FilesAccess.EnsureUserSubject(
            AccessSubject.Service("adapter-a"),
            new TestScopeContext(IsEnabled: false, ScopeId: null));

        Assert.Equal(FilesApplicationErrors.ScopeRequired, missingScope.Error);
        Assert.Equal(FilesApplicationErrors.AccessDenied, serviceSubject.Error);
    }

    [Fact]
    public async Task Upload_writes_only_the_current_namespace_and_round_trips()
    {
        string root = NewStorageRoot();
        Guid fileId = Guid.Parse("019f4a7c-1000-7000-8000-000000000002");

        try
        {
            using ServiceProvider provider = BuildProvider(root, fileId);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope, "scope-a");
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            IFileStorage storage = serviceScope.ServiceProvider.GetRequiredService<IFileStorage>();
            AccessSubject subject = AccessSubject.User("user-a");
            byte[] payload = Encoding.UTF8.GetBytes("current-object");

            await using MemoryStream content = new(payload);
            Result<FileUploadResponse> upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                payload.Length,
                "text/plain",
                "current.txt",
                subject));

            Assert.True(upload.IsSuccess, upload.Error.Message);
            FileStorageObjectKey currentKey = FilesStorageKeys.For(
                fileId,
                subject,
                serviceScope.ServiceProvider.GetRequiredService<IScopeContext>());
            FileStorageObjectKey legacyKey = FilesStorageKeys.LegacyFor(
                fileId,
                subject,
                serviceScope.ServiceProvider.GetRequiredService<IScopeContext>());
            Assert.NotNull(await storage.GetPropertiesAsync(currentKey));
            Assert.Null(await storage.GetPropertiesAsync(legacyKey));

            Result<FileDownload> download = await dispatcher.QueryAsync(new GetFileQuery(fileId, subject));
            Assert.True(download.IsSuccess, download.Error.Message);
            await using MemoryStream downloaded = new();
            await download.Value.File.CopyToAsync(downloaded);
            Assert.Equal(payload, downloaded.ToArray());
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task Legacy_objects_remain_readable_and_deletable_by_the_original_owner()
    {
        string root = NewStorageRoot();
        Guid fileId = Guid.Parse("019f4a7c-1000-7000-8000-000000000003");

        try
        {
            using ServiceProvider provider = BuildProvider(root, fileId);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope, "scope-a");
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            IFileStorage storage = serviceScope.ServiceProvider.GetRequiredService<IFileStorage>();
            IScopeContext scopeContext = serviceScope.ServiceProvider.GetRequiredService<IScopeContext>();
            AccessSubject subject = AccessSubject.User("user-a");
            FileStorageObjectKey legacyKey = FilesStorageKeys.LegacyFor(fileId, subject, scopeContext);
            byte[] payload = Encoding.UTF8.GetBytes("legacy-object");

            await using (MemoryStream content = new(payload))
            {
                await storage.PutAsync(new FileStorageWriteRequest(
                    legacyKey,
                    content,
                    payload.Length,
                    "text/plain",
                    "legacy.txt"));
            }

            Result<FileDownload> download = await dispatcher.QueryAsync(new GetFileQuery(fileId, subject));
            Assert.True(download.IsSuccess, download.Error.Message);
            await using MemoryStream downloaded = new();
            await download.Value.File.CopyToAsync(downloaded);
            Assert.Equal(payload, downloaded.ToArray());

            Result<Unit> delete = await dispatcher.SendAsync(new DeleteFileCommand(fileId, subject));
            Assert.True(delete.IsSuccess, delete.Error.Message);
            Assert.Null(await storage.GetPropertiesAsync(legacyKey));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Theory]
    [InlineData(FileContentInspectionStatus.Rejected, "Files.ContentRejected")]
    [InlineData(FileContentInspectionStatus.Unavailable, "Files.ContentInspectionRequired")]
    public async Task Required_inspection_must_approve_content_before_storage(
        FileContentInspectionStatus status,
        string expectedErrorCode)
    {
        string root = NewStorageRoot();
        RecordingInspector inspector = new(status);

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                Guid.Parse("019f4a7c-1000-7000-8000-000000000004"),
                requireInspection: true,
                inspector);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope, "scope-a");
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            byte[] payload = Encoding.UTF8.GetBytes("inspect-me");
            await using MemoryStream content = new(payload);

            Result<FileUploadResponse> upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                payload.Length,
                "text/plain",
                "inspection.txt",
                AccessSubject.User("user-a")));

            Assert.True(upload.IsFailure);
            Assert.Equal(expectedErrorCode, upload.Error.Code);
            Assert.Equal(payload, inspector.Content);
            Assert.False(ContainsStoredFiles(root));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task Upload_rejects_an_oversized_declaration_before_inspection()
    {
        string root = NewStorageRoot();
        RecordingInspector inspector = new(FileContentInspectionStatus.Clean);

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                Guid.Parse("019f4a7c-1000-7000-8000-000000000006"),
                requireInspection: true,
                inspector,
                maximumObjectBytes: 4);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope, "scope-a");
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await using MemoryStream content = new(Encoding.UTF8.GetBytes("large"));

            Result<FileUploadResponse> upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                content.Length,
                "text/plain",
                "large.txt",
                AccessSubject.User("user-a")));

            Assert.Equal(FilesApplicationErrors.FileTooLarge, upload.Error);
            Assert.Empty(inspector.Content);
            Assert.False(ContainsStoredFiles(root));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task Inspection_rejects_content_that_does_not_match_the_declared_length()
    {
        string root = NewStorageRoot();
        RecordingInspector inspector = new(FileContentInspectionStatus.Clean);

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                Guid.Parse("019f4a7c-1000-7000-8000-000000000007"),
                requireInspection: true,
                inspector);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope, "scope-a");
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await using MemoryStream content = new(Encoding.UTF8.GetBytes("short"));

            Result<FileUploadResponse> upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                content.Length + 1,
                "text/plain",
                "mismatch.txt",
                AccessSubject.User("user-a")));

            Assert.Equal(FilesApplicationErrors.ContentLengthMismatch, upload.Error);
            Assert.Empty(inspector.Content);
            Assert.False(ContainsStoredFiles(root));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task Clean_inspection_rewinds_and_stores_the_exact_content()
    {
        string root = NewStorageRoot();
        RecordingInspector inspector = new(FileContentInspectionStatus.Clean);

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                Guid.Parse("019f4a7c-1000-7000-8000-000000000005"),
                requireInspection: true,
                inspector);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope, "scope-a");
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            byte[] payload = Encoding.UTF8.GetBytes("approved-content");
            await using MemoryStream content = new(payload);

            Result<FileUploadResponse> upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                payload.Length,
                "text/plain",
                "approved.txt",
                AccessSubject.User("user-a")));
            Assert.True(upload.IsSuccess, upload.Error.Message);

            Result<FileDownload> download = await dispatcher.QueryAsync(
                new GetFileQuery(upload.Value.FileId, AccessSubject.User("user-a")));
            Assert.True(download.IsSuccess, download.Error.Message);
            await using MemoryStream downloaded = new();
            await download.Value.File.CopyToAsync(downloaded);

            Assert.Equal(payload, inspector.Content);
            Assert.Equal(payload, downloaded.ToArray());
            Assert.Equal("test-inspector", download.Value.File.Properties.Metadata["content-inspector"]);
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    private static ServiceProvider BuildProvider(
        string root,
        Guid fileId,
        bool requireInspection = false,
        IFileContentInspector? inspector = null,
        long maximumObjectBytes = 1_048_576)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["ApplicationIdentity:Namespace"] = "files-tests";
        builder.Configuration["Scoping:Enabled"] = "true";
        builder.Configuration["Scoping:HeaderName"] = "X-Scope-Id";
        builder.Configuration["Scoping:LocalDefaultScopeId"] = "scope-a";
        builder.Configuration["FileManagement:Enabled"] = "true";
        builder.Configuration["FileManagement:Provider"] = "LocalStorage";
        builder.Configuration["FileManagement:MaximumObjectBytes"] = maximumObjectBytes.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        builder.Configuration["FileManagement:AllowedContentTypes:0"] = "text/plain";
        builder.Configuration["Files:Uploads:RequireContentInspection"] = requireInspection.ToString();
        builder.Configuration["FileManagement:LocalStorage:RootPath"] = root;
        builder.Services.AddSingleton<IIdGenerator>(new FixedIdGenerator(fileId));
        if (inspector is not null)
        {
            builder.Services.AddSingleton(inspector);
        }

        builder.AddScopingInfrastructure();
        builder.AddCqrsInfrastructure();
        builder.AddLocalFileStorage();
        builder.AddModule<FilesModule>();
        builder.ValidateModuleComposition();
        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void SetScope(IServiceScope serviceScope, string scopeId) =>
        serviceScope.ServiceProvider.GetRequiredService<IScopeContextAccessor>().SetScope(scopeId);

    private static string NewStorageRoot() =>
        Path.Combine(Path.GetTempPath(), $"gma-files-tests-{Guid.NewGuid():N}");

    private static void DeleteStorageRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool ContainsStoredFiles(string root) =>
        Directory.Exists(root) && Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any();

    private sealed record TestScopeContext(bool IsEnabled, string? ScopeId) : IScopeContext;

    private sealed class FixedIdGenerator(Guid fileId) : IIdGenerator
    {
        public Guid NewId() => fileId;
    }

    private sealed class RecordingInspector(FileContentInspectionStatus status) : IFileContentInspector
    {
        public byte[] Content { get; private set; } = [];

        public async ValueTask<FileContentInspectionResult> InspectAsync(
            FileContentInspectionRequest request,
            CancellationToken cancellationToken)
        {
            await using MemoryStream copy = new();
            await request.Content.CopyToAsync(copy, cancellationToken);
            this.Content = copy.ToArray();
            return status switch
            {
                FileContentInspectionStatus.Clean => FileContentInspectionResult.Clean("test-inspector"),
                FileContentInspectionStatus.Rejected => FileContentInspectionResult.Rejected("test-inspector"),
                _ => FileContentInspectionResult.Unavailable("test-inspector")
            };
        }
    }
}
