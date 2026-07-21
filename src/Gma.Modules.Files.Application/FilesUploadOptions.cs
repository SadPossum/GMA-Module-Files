namespace Gma.Modules.Files.Application;

public sealed class FilesUploadOptions
{
    public const string SectionName = "Files:Uploads";

    public bool RequireTrustedContentType { get; set; }
    public bool RequireContentInspection { get; set; }
}
