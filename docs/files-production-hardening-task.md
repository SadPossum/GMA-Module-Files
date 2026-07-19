# Files Production Hardening Task

Status: in progress
Date: 2026-07-19

## Goal

Make the optional private-user Files front door production-ready without moving product file lifecycle, public delivery, cross-user sharing, or provider-specific behavior into the module.

## Ownership Boundary

- Framework owns provider-neutral object storage contracts, shared options and concrete LocalStorage/MinIO adapters;
- Files owns authenticated private-user upload, download and delete behavior over those contracts;
- product modules own business records that reference objects, retention, legal holds, public delivery, quotas and orphan cleanup;
- cross-module workflows belong in an explicit GMA Extension rather than a direct reusable-module dependency;
- hosts own edge request limits, rate limiting, scanner availability, object-store policy, credentials and operational monitoring.

## Audit Baseline

- uploads are bounded and optionally copied to a delete-on-close temporary file before fail-closed content inspection;
- file access is isolated by active scope, authenticated user subject and opaque file id;
- new storage namespaces use only a 64-bit prefix of each SHA-256 scope and subject digest;
- application errors still call a generic scope a tenant;
- private downloads do not set an attachment file name or module-local cache and content-sniffing protections;
- the MinIO adapter selects a synchronous callback overload while copying downloads and therefore blocks a worker and does not pass cancellation to the stream copy;
- shared options validation assumes `AllowedContentTypes` is non-null and can throw instead of reporting invalid configuration;
- the repository has no direct tests, no reusable-module boundary guard, no Linux CI leg and no explicit package vulnerability audit;
- buffered `IFormFile` uploads are suitable for bounded objects, but hosts that raise the object limit above their server request-body limit must raise that edge limit deliberately.

## Delivery Slice

1. Make Framework options validation reject a null content-type allowlist and make MinIO download copies asynchronous and cancellation-aware.
2. Write new Files objects under a versioned namespace using complete SHA-256 scope and subject digests.
3. Preserve read and delete compatibility for objects written under the legacy truncated namespace; do not write new legacy objects.
4. Replace tenant-specific application error naming with generic scope language.
5. Return private downloads as attachment-safe, non-cacheable, non-sniffable responses with a normalized file name when present.
6. Add focused tests for metadata, registration, limits, inspection, namespace isolation and compatibility, authenticated API isolation, error mapping and download headers.
7. Add a repository-local reusable-module boundary guard, Windows and Linux CI, and an explicit transitive package vulnerability audit.
8. Document buffering, edge request-size, content inspection, quota and lifecycle responsibilities.
9. Publish exact Framework and Files heads, then verify GMA Skeleton and BunkFy against those heads.

## Non-Goals

- a persistent file catalog or a module-owned relational projection;
- product-specific purposes, attachment relations, retention schedules, quotas or legal holds;
- public URLs, presigned object-store URLs, inline browser delivery or cross-user sharing;
- a generic malware scanner implementation;
- transparent bulk migration, which is impossible without the product-owned references that identify legacy objects.

## Acceptance Criteria

- invalid shared file options fail through options validation rather than a null-reference exception;
- MinIO download copies use the SDK asynchronous callback and propagate cancellation;
- newly stored object keys contain a version segment and full scope and subject digests;
- existing legacy objects remain readable and deletable by their original owner and scope;
- scope-enabled internal operations fail with `Files.ScopeRequired` when no active scope exists;
- successful downloads use the stored normalized file name, force attachment handling, disable caching and disable MIME sniffing;
- another user or scope cannot read or delete an object through the API;
- required unavailable or rejecting inspection fails before storage, while a clean inspection stores the exact bytes;
- source projects reference no other reusable module and contain no product-specific source;
- standalone build, tests, boundary checks and package audit pass on Windows and Linux;
- Skeleton and BunkFy pass against the exact published Framework and Files heads.

## Completion Evidence

Pending.
