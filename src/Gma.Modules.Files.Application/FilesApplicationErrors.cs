namespace Gma.Modules.Files.Application;

using Gma.Framework.Results;

public static class FilesApplicationErrors
{
    public static readonly Error ScopeRequired = new(
        "Files.ScopeRequired",
        "An active scope is required for file operations.");

    public static readonly Error AccessDenied = new(
        "Files.AccessDenied",
        "The current subject cannot access this file.");

    public static readonly Error FileRequired = new(
        "Files.FileRequired",
        "A file is required.");

    public static readonly Error FileEmpty = new(
        "Files.FileEmpty",
        "The uploaded file is empty.");

    public static readonly Error FileTooLarge = new(
        "Files.FileTooLarge",
        "The uploaded file exceeds the configured maximum length.");

    public static readonly Error ContentTypeNotAllowed = new(
        "Files.ContentTypeNotAllowed",
        "The uploaded file content type is not allowed.");

    public static readonly Error ContentTypeDetectionRequired = new(
        "Files.ContentTypeDetectionRequired",
        "The configured content-type detector could not classify the uploaded file.");

    public static readonly Error ContentTypeUnrecognized = new(
        "Files.ContentTypeUnrecognized",
        "The uploaded file content type could not be recognized.");

    public static readonly Error ContentInspectionRequired = new(
        "Files.ContentInspectionRequired",
        "The configured content inspection service could not approve the uploaded file.");

    public static readonly Error ContentRejected = new(
        "Files.ContentRejected",
        "The uploaded file was rejected by content inspection.");

    public static readonly Error ContentLengthMismatch = new(
        "Files.ContentLengthMismatch",
        "The uploaded content length does not match the declared length.");

    public static readonly Error FileIdInvalid = new(
        "Files.FileIdInvalid",
        "The file id is invalid.");

    public static readonly Error FileNotFound = new(
        "Files.FileNotFound",
        "The file was not found.");
}
