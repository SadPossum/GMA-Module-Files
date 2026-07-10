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
        services.AddApplicationServicesFromAssembly(typeof(DependencyInjection).Assembly);
        services.TryAddSingleton<IFileContentInspector, UnavailableFileContentInspector>();

        return services;
    }

    private static bool IsValidFileManagementOptions(FileManagementOptions options) =>
        FileManagementOptionsValidation.Validate(options).Length == 0;
}
