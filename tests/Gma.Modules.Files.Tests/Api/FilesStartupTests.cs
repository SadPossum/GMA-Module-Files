namespace Gma.Modules.Files.Tests;

using Gma.Framework.Api.Modules;
using Gma.Framework.Cqrs.Infrastructure;
using Gma.Framework.FileManagement;
using Gma.Framework.FileManagement.LocalStorage;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Scoping.Infrastructure;
using Gma.Modules.Files.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Integration")]
public sealed class FilesStartupTests
{
    [Fact]
    public async Task Production_host_rejects_unsafe_upload_policy_before_startup()
    {
        using IHost host = BuildHost(
            requireTrustedContentType: false,
            requireInspection: false,
            detector: null,
            inspector: null);

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());

        Assert.Contains("RequireTrustedContentType must be true", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RequireContentInspection must be true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Required_unavailable_capability_fails_host_startup()
    {
        StartupDetector detector = new(isReady: false);
        StartupInspector inspector = new(isReady: true);
        using IHost host = BuildHost(
            requireTrustedContentType: true,
            requireInspection: true,
            detector,
            inspector);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync());

        Assert.Contains("content-type detector", exception.Message, StringComparison.Ordinal);
        Assert.Contains("test-detector", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_host_starts_only_with_safe_policy_and_ready_capabilities()
    {
        StartupDetector detector = new(isReady: true);
        StartupInspector inspector = new(isReady: true);
        using IHost host = BuildHost(
            requireTrustedContentType: true,
            requireInspection: true,
            detector,
            inspector);

        await host.StartAsync();
        await host.StopAsync();
    }

    private static IHost BuildHost(
        bool requireTrustedContentType,
        bool requireInspection,
        StartupDetector? detector,
        StartupInspector? inspector)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["ApplicationIdentity:Namespace"] = "files-startup-tests";
        builder.Configuration["Scoping:Enabled"] = "true";
        builder.Configuration["Scoping:HeaderName"] = "X-Scope-Id";
        builder.Configuration["Scoping:LocalDefaultScopeId"] = "scope-a";
        builder.Configuration["FileManagement:Enabled"] = "true";
        builder.Configuration["FileManagement:Provider"] = "LocalStorage";
        builder.Configuration["FileManagement:MaximumObjectBytes"] = "1048576";
        builder.Configuration["FileManagement:AllowedContentTypes:0"] = "text/plain";
        builder.Configuration["FileManagement:LocalStorage:RootPath"] = Path.Combine(
            Path.GetTempPath(),
            $"gma-files-startup-{Guid.NewGuid():N}");
        builder.Configuration["Files:Uploads:RequireTrustedContentType"] =
            requireTrustedContentType.ToString();
        builder.Configuration["Files:Uploads:RequireContentInspection"] = requireInspection.ToString();

        if (detector is not null)
        {
            builder.Services.AddSingleton<IFileContentTypeDetector>(detector);
            builder.Services.AddSingleton<IFileContentTypeDetectorReadiness>(detector);
        }

        if (inspector is not null)
        {
            builder.Services.AddSingleton<IFileContentInspector>(inspector);
            builder.Services.AddSingleton<IFileContentInspectorReadiness>(inspector);
        }

        builder.AddScopingInfrastructure();
        builder.AddCqrsInfrastructure();
        builder.AddLocalFileStorage();
        builder.AddModule<FilesModule>();
        builder.ValidateModuleComposition();
        return builder.Build();
    }

    private sealed class StartupDetector(bool isReady) :
        IFileContentTypeDetector,
        IFileContentTypeDetectorReadiness
    {
        public ValueTask<FileContentTypeDetectionResult> DetectAsync(
            FileContentTypeDetectionRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(FileContentTypeDetectionResult.Detected("test-detector", "text/plain"));

        public ValueTask<FileContentCapabilityReadiness> CheckReadinessAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(isReady
                ? FileContentCapabilityReadiness.Ready("test-detector")
                : FileContentCapabilityReadiness.Unavailable("test-detector"));
    }

    private sealed class StartupInspector(bool isReady) :
        IFileContentInspector,
        IFileContentInspectorReadiness
    {
        public ValueTask<FileContentInspectionResult> InspectAsync(
            FileContentInspectionRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(FileContentInspectionResult.Clean("test-inspector"));

        public ValueTask<FileContentCapabilityReadiness> CheckReadinessAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(isReady
                ? FileContentCapabilityReadiness.Ready("test-inspector")
                : FileContentCapabilityReadiness.Unavailable("test-inspector"));
    }
}
