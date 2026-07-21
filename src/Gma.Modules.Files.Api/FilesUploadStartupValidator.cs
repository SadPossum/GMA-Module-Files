namespace Gma.Modules.Files.Api;

using Gma.Framework.FileManagement;
using Gma.Modules.Files.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

internal sealed class FilesUploadStartupValidator(
    IHostEnvironment environment,
    IOptions<FileManagementOptions> fileManagementOptions,
    IOptions<FilesUploadOptions> uploadOptions,
    IFileContentTypeDetectorReadiness contentTypeDetectorReadiness,
    IFileContentInspectorReadiness contentInspectorReadiness) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        FilesUploadOptions policy = uploadOptions.Value;
        if (environment.IsProduction())
        {
            List<string> failures = [];
            if (!policy.RequireTrustedContentType)
            {
                failures.Add($"{FilesUploadOptions.SectionName}:RequireTrustedContentType must be true in Production.");
            }

            if (!policy.RequireContentInspection)
            {
                failures.Add($"{FilesUploadOptions.SectionName}:RequireContentInspection must be true in Production.");
            }

            if (fileManagementOptions.Value.AllowedContentTypes is not { Length: > 0 })
            {
                failures.Add($"{FileManagementOptions.SectionName}:AllowedContentTypes must be non-empty when Files runs in Production.");
            }

            if (failures.Count > 0)
            {
                throw new OptionsValidationException(
                    FilesUploadOptions.SectionName,
                    typeof(FilesUploadOptions),
                    failures);
            }
        }

        if (policy.RequireTrustedContentType)
        {
            FileContentCapabilityReadiness readiness = await contentTypeDetectorReadiness
                .CheckReadinessAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!readiness.IsReady)
            {
                throw new InvalidOperationException(
                    $"Files requires a ready content-type detector; '{readiness.Provider}' reported unavailable.");
            }
        }

        if (policy.RequireContentInspection)
        {
            FileContentCapabilityReadiness readiness = await contentInspectorReadiness
                .CheckReadinessAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!readiness.IsReady)
            {
                throw new InvalidOperationException(
                    $"Files requires a ready content inspector; '{readiness.Provider}' reported unavailable.");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
