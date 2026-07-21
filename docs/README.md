# Files Module

Related delivery records:

- [Files production hardening task](files-production-hardening-task.md)
- [Trusted content gate task](trusted-content-gate-task.md)

`Files` is an optional scope-aware API front door over shared file storage.

The module profile requires:

- `scoping.context`
- `file-management.storage`

It provides:

- `files.objects`

Use it only when a host deliberately wants a centralized upload/download/delete surface for bounded private user files and can supply the required production content controls. Feature modules use `Gma.Framework.FileManagement` directly when they own public files, cross-user sharing, or business-specific file lifecycle rules.

## Endpoints

```text
POST   /api/files
GET    /api/files/{fileId}
DELETE /api/files/{fileId}
```

All endpoints require authorization. When scoping is enabled, the configured scope header is required and must match the authenticated token scope claim. Storage keys are isolated by complete SHA-256 scope and caller-subject digests plus an opaque file id, so another authenticated user in the same scope cannot read or delete a private file by guessing its id.

New objects use the versioned `files/v2` namespace. Reads and deletes also check the legacy truncated-digest namespace so existing objects remain usable, but the module never writes new legacy objects. A product that needs to remove legacy compatibility must migrate its referenced objects before disabling that fallback.

Downloads are private attachment responses: the stored normalized file name is emitted when present, caching is disabled, and MIME sniffing is disabled. Public URLs, cross-user sharing and inline browser delivery require product-owned endpoints and policy rather than weakening this private front door.

## Composition Example

```csharp
builder.AddLocalFileStorage();
builder.AddModule<FilesModule>();
builder.ValidateModuleComposition();
```

For MinIO, use `builder.AddMinioFileStorage()` instead, or register both adapters and let `FileManagement:Provider` select one.

## Content Inspection And Lifecycle

Files owns upload policy separately from shared storage configuration:

```json
{
  "Files": {
    "Uploads": {
      "RequireTrustedContentType": true,
      "RequireContentInspection": true
    }
  }
}
```

Production requires both switches, a non-empty `FileManagement:AllowedContentTypes` list, a ready `IFileContentTypeDetector` plus `IFileContentTypeDetectorReadiness`, and a ready `IFileContentInspector` plus `IFileContentInspectorReadiness`. The module fails host startup when this safety floor is incomplete. GMA supplies unavailable fail-closed defaults, not a MIME library or malware scanner; the host must provide reviewed adapters.

When either content gate is enabled, Files copies the upload once into a bounded delete-on-close temporary quarantine file and verifies its exact declared length. Trusted detection runs first and controls allowlisting plus the stored content type; the caller declaration cannot bypass policy. Inspection then sees the same rewound bytes. Only recognized, allowed, `Clean` content reaches storage. Unknown, disallowed, rejected, unavailable, mismatched, oversized, canceled, or failed uploads leave no Files object.

Storage metadata records the trusted content type, length, normalized file name, module identity, detector identity, and inspector identity. Product modules still own business retention, legal holds, public sharing, and orphan cleanup because those policies depend on the record that references the object. Delete through `IFileStorage` only after the owning module has made that lifecycle decision.

## Host Limits And Operations

The front door uses buffered `IFormFile` parsing and is intended for bounded private objects. `FileManagement:MaximumObjectBytes` also configures the per-multipart-section form limit. If a host raises that value above its web server or reverse-proxy request-body limit, it must raise the edge limit deliberately; GMA does not change a host-wide Kestrel, IIS or proxy setting from inside this module.

Production hosts must also compose edge rate limiting, a storage quota or cost policy appropriate to the product, detector/scanner monitoring, private object-store access, credential rotation and product-owned orphan cleanup. Concrete detector/scanner conformance, including EICAR, archive-bomb, polyglot and spoofed-format cases, belongs to the selected adapters. Large media, resumable uploads and direct-to-object-store flows belong in a product module or adapter with their own authorization and lifecycle.
