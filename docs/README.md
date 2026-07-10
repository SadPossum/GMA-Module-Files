# Files Module

`Files` is an optional scope-aware API front door over shared file storage.

The module profile requires:

- `scoping.context`
- `file-management.storage`

It provides:

- `files.objects`

Use it when a host wants a centralized upload/download/delete surface for private user files such as profile images, attachments, imports, or exports. Feature modules can bypass the front door and use `Gma.Framework.FileManagement` directly when they own public files, cross-user sharing, or business-specific file lifecycle rules.

## Endpoints

```text
POST   /api/files
GET    /api/files/{fileId}
DELETE /api/files/{fileId}
```

All endpoints require authorization. When scoping is enabled, the configured scope header is required and must match the authenticated token tenant claim in tenant-aware hosts. Storage keys are scoped by scope hash and caller subject hash, so another authenticated user in the same tenant/scope cannot read or delete a private file by guessing its id.

## Composition Example

```csharp
builder.AddLocalFileStorage();
builder.AddModule<FilesModule>();
builder.ValidateModuleComposition();
```

For MinIO, use `builder.AddMinioFileStorage()` instead, or register both adapters and let `FileManagement:Provider` select one.

## Content Inspection And Lifecycle

Set `FileManagement:RequireContentInspection=true` in production and replace `IFileContentInspector` with a malware/content scanner adapter. Required inspection copies the upload into a bounded delete-on-close temporary file, verifies the declared length, scans the rewindable content, and stores bytes only after a `Clean` result. An unavailable scanner fails closed; development may explicitly set the option to `false`.

Storage metadata already records validated content type, length, file name, module metadata, and the inspector name. Product modules still own business retention, legal holds, public sharing, and orphan cleanup because those policies depend on the record that references the object. Delete through `IFileStorage` only after the owning module has made that lifecycle decision.
