namespace Gma.Modules.Files.Contracts;

using Gma.Framework.FileManagement;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Scoping;

public static class FilesProfiles
{
    public const string DefaultName = "default";

    public static ModuleProfileDescriptor Default { get; } = new(
        FilesModuleMetadata.Name,
        DefaultName,
        provides:
        [
            FilesCompositionFeatures.ObjectsProvided(Provider(DefaultName))
        ],
        requires:
        [
            new RequiredCompositionFeature(
                ScopeCompositionFeatures.Context,
                Provider(DefaultName),
                reason: "Files are scoped when scoping is enabled; register scoping infrastructure or a tenancy bridge."),
            new RequiredCompositionFeature(
                new CompositionFeatureId(FileManagementCompositionFeatures.Storage),
                Provider(DefaultName),
                reason: "Files stores bytes through Gma.Framework.FileManagement; register a concrete adapter such as LocalStorage or MinIO.")
        ],
        displayName: "Files default",
        description: "Scope-aware file upload, download, and delete front door backed by shared file storage.");

    private static string Provider(string profileName) => $"{FilesModuleMetadata.Name}/{profileName}";
}
