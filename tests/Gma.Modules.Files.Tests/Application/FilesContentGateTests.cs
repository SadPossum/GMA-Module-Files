namespace Gma.Modules.Files.Tests;

using System.Text;
using Gma.Framework.Api.Modules;
using Gma.Framework.Cqrs;
using Gma.Framework.Cqrs.Infrastructure;
using Gma.Framework.FileManagement;
using Gma.Framework.FileManagement.LocalStorage;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Scoping;
using Gma.Framework.Scoping.Infrastructure;
using Gma.Modules.Files.Api;
using Gma.Modules.Files.Application.Commands;
using Gma.Modules.Files.Application.Queries;
using Gma.Modules.Files.Application.ReadModels;
using Gma.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[Trait("Category", "Unit")]
public sealed class FilesContentGateTests
{
    [Fact]
    public async Task Trusted_detection_prevents_a_declared_type_from_bypassing_the_allowlist()
    {
        string root = NewStorageRoot();
        RecordingDetector detector = new(
            FileContentTypeDetectionResult.Detected("test-detector", "application/json"));

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                requireTrustedContentType: true,
                detector: detector,
                allowedContentTypes: ["text/plain"]);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope);
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            byte[] payload = Encoding.UTF8.GetBytes("not-really-text");
            await using MemoryStream content = new(payload);

            var upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                payload.Length,
                "text/plain",
                "spoofed.txt",
                Gma.Framework.AccessControl.AccessSubject.User("user-a")));

            Assert.Equal("Files.ContentTypeNotAllowed", upload.Error.Code);
            Assert.Equal(payload, detector.Content);
            Assert.False(ContainsStoredFiles(root));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Theory]
    [InlineData(FileContentTypeDetectionStatus.Unrecognized, "Files.ContentTypeUnrecognized")]
    [InlineData(FileContentTypeDetectionStatus.Unavailable, "Files.ContentTypeDetectionRequired")]
    public async Task Trusted_detection_fails_closed_without_a_detected_type(
        FileContentTypeDetectionStatus status,
        string expectedErrorCode)
    {
        string root = NewStorageRoot();
        RecordingDetector detector = new(status switch
        {
            FileContentTypeDetectionStatus.Unrecognized =>
                FileContentTypeDetectionResult.Unrecognized("test-detector"),
            _ => FileContentTypeDetectionResult.Unavailable("test-detector")
        });

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                requireTrustedContentType: true,
                detector: detector);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope);
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await using MemoryStream content = new(Encoding.UTF8.GetBytes("unknown"));

            var upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                content.Length,
                "text/plain",
                "unknown.txt",
                Gma.Framework.AccessControl.AccessSubject.User("user-a")));

            Assert.Equal(expectedErrorCode, upload.Error.Code);
            Assert.False(ContainsStoredFiles(root));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task Detection_and_inspection_share_exact_quarantined_bytes_and_store_the_trusted_type()
    {
        string root = NewStorageRoot();
        RecordingDetector detector = new(
            FileContentTypeDetectionResult.Detected("test-detector", "text/plain"));
        RecordingInspector inspector = new(FileContentInspectionResult.Clean("test-inspector"));

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                requireTrustedContentType: true,
                detector: detector,
                requireInspection: true,
                inspector: inspector);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope);
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            byte[] payload = Encoding.UTF8.GetBytes("trusted-content");
            await using MemoryStream content = new(payload);

            var upload = await dispatcher.SendAsync(new UploadFileCommand(
                content,
                payload.Length,
                "application/octet-stream",
                "trusted.txt",
                Gma.Framework.AccessControl.AccessSubject.User("user-a")));
            Assert.True(upload.IsSuccess, upload.Error.Message);

            var download = await dispatcher.QueryAsync(new GetFileQuery(
                upload.Value.FileId,
                Gma.Framework.AccessControl.AccessSubject.User("user-a")));
            Assert.True(download.IsSuccess, download.Error.Message);
            await using MemoryStream stored = new();
            await download.Value.File.CopyToAsync(stored);

            Assert.Equal(payload, detector.Content);
            Assert.Equal(payload, inspector.Content);
            Assert.Equal("text/plain", inspector.ContentType);
            Assert.Equal(payload, stored.ToArray());
            Assert.Equal("text/plain", download.Value.File.Properties.ContentType);
            Assert.Equal(
                "test-detector",
                download.Value.File.Properties.Metadata["content-type-detector"]);
            Assert.Equal(
                "test-inspector",
                download.Value.File.Properties.Metadata["content-inspector"]);
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task Provider_exceptions_delete_the_quarantine_without_storing_an_object()
    {
        string root = NewStorageRoot();
        ThrowingDetector detector = new();

        try
        {
            using ServiceProvider provider = BuildProvider(
                root,
                requireTrustedContentType: true,
                detector: detector);
            using IServiceScope serviceScope = provider.CreateScope();
            SetScope(serviceScope);
            IRequestDispatcher dispatcher = serviceScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await using MemoryStream content = new(Encoding.UTF8.GetBytes("provider-failure"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync(new UploadFileCommand(
                content,
                content.Length,
                "text/plain",
                "failure.txt",
                Gma.Framework.AccessControl.AccessSubject.User("user-a"))));

            Assert.NotNull(detector.QuarantinePath);
            Assert.False(File.Exists(detector.QuarantinePath));
            Assert.False(ContainsStoredFiles(root));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    private static ServiceProvider BuildProvider(
        string root,
        bool requireTrustedContentType = false,
        IFileContentTypeDetector? detector = null,
        bool requireInspection = false,
        IFileContentInspector? inspector = null,
        string[]? allowedContentTypes = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["ApplicationIdentity:Namespace"] = "files-content-gate-tests";
        builder.Configuration["Scoping:Enabled"] = "true";
        builder.Configuration["Scoping:HeaderName"] = "X-Scope-Id";
        builder.Configuration["Scoping:LocalDefaultScopeId"] = "scope-a";
        builder.Configuration["FileManagement:Enabled"] = "true";
        builder.Configuration["FileManagement:Provider"] = "LocalStorage";
        builder.Configuration["FileManagement:MaximumObjectBytes"] = "1048576";
        builder.Configuration["FileManagement:LocalStorage:RootPath"] = root;
        builder.Configuration["Files:Uploads:RequireTrustedContentType"] =
            requireTrustedContentType.ToString();
        builder.Configuration["Files:Uploads:RequireContentInspection"] = requireInspection.ToString();

        string[] contentTypes = allowedContentTypes ?? ["text/plain"];
        for (int index = 0; index < contentTypes.Length; index++)
        {
            builder.Configuration[$"FileManagement:AllowedContentTypes:{index}"] = contentTypes[index];
        }

        builder.Services.AddSingleton<IIdGenerator>(
            new FixedIdGenerator(Guid.Parse("019f4a7c-1000-7000-8000-000000000020")));
        if (detector is not null)
        {
            builder.Services.AddSingleton(detector);
        }

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

    private static void SetScope(IServiceScope serviceScope) =>
        serviceScope.ServiceProvider.GetRequiredService<IScopeContextAccessor>().SetScope("scope-a");

    private static string NewStorageRoot() =>
        Path.Combine(Path.GetTempPath(), $"gma-files-content-gate-{Guid.NewGuid():N}");

    private static void DeleteStorageRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool ContainsStoredFiles(string root) =>
        Directory.Exists(root) && Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any();

    private sealed class FixedIdGenerator(Guid fileId) : IIdGenerator
    {
        public Guid NewId() => fileId;
    }

    private sealed class RecordingDetector(FileContentTypeDetectionResult result) : IFileContentTypeDetector
    {
        public byte[] Content { get; private set; } = [];

        public async ValueTask<FileContentTypeDetectionResult> DetectAsync(
            FileContentTypeDetectionRequest request,
            CancellationToken cancellationToken)
        {
            await using MemoryStream copy = new();
            await request.Content.CopyToAsync(copy, cancellationToken);
            this.Content = copy.ToArray();
            return result;
        }
    }

    private sealed class RecordingInspector(FileContentInspectionResult result) : IFileContentInspector
    {
        public byte[] Content { get; private set; } = [];
        public string? ContentType { get; private set; }

        public async ValueTask<FileContentInspectionResult> InspectAsync(
            FileContentInspectionRequest request,
            CancellationToken cancellationToken)
        {
            await using MemoryStream copy = new();
            await request.Content.CopyToAsync(copy, cancellationToken);
            this.Content = copy.ToArray();
            this.ContentType = request.ContentType;
            return result;
        }
    }

    private sealed class ThrowingDetector : IFileContentTypeDetector
    {
        public string? QuarantinePath { get; private set; }

        public ValueTask<FileContentTypeDetectionResult> DetectAsync(
            FileContentTypeDetectionRequest request,
            CancellationToken cancellationToken)
        {
            this.QuarantinePath = Assert.IsType<FileStream>(request.Content).Name;
            throw new InvalidOperationException("Detector failed.");
        }
    }
}
