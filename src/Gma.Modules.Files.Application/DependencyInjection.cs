namespace Gma.Modules.Files.Application;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Gma.Framework.Application.Composition;
using Gma.Framework.FileManagement;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddFilesApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<FileManagementOptions>()
            .Bind(configuration.GetSection(FileManagementOptions.SectionName))
            .Validate(IsValidFileManagementOptions, FileManagementOptionsValidation.FailureMessage)
            .ValidateOnStart();
        services
            .AddOptions<FilesUploadOptions>()
            .Bind(configuration.GetSection(FilesUploadOptions.SectionName))
            .ValidateOnStart();
        services.AddApplicationServicesFromAssembly(typeof(DependencyInjection).Assembly);
        services.TryAddSingleton<UnavailableFileContentInspector>();
        services.TryAddSingleton<IFileContentInspector>(provider =>
            provider.GetRequiredService<UnavailableFileContentInspector>());
        services.TryAddSingleton<IFileContentInspectorReadiness>(provider =>
            provider.GetRequiredService<IFileContentInspector>() as IFileContentInspectorReadiness ??
            provider.GetRequiredService<UnavailableFileContentInspector>());
        services.TryAddSingleton<UnavailableFileContentTypeDetector>();
        services.TryAddSingleton<IFileContentTypeDetector>(provider =>
            provider.GetRequiredService<UnavailableFileContentTypeDetector>());
        services.TryAddSingleton<IFileContentTypeDetectorReadiness>(provider =>
            provider.GetRequiredService<IFileContentTypeDetector>() as IFileContentTypeDetectorReadiness ??
            provider.GetRequiredService<UnavailableFileContentTypeDetector>());
        services.TryAddScoped<FileUploadContentGate>();

        return services;
    }

    private static bool IsValidFileManagementOptions(FileManagementOptions options) =>
        FileManagementOptionsValidation.Validate(options).Length == 0;
}
