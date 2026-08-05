# Files Boundary Revalidation Task

Status: completed
Date: 2026-08-05
Completed: 2026-08-05

## Goal

Close the remaining compatibility, quarantine, and HTTP boundary gaps in the optional private-user Files front door while preserving its deliberately small, storage-backed design.

## Findings

- deleting a current object skipped the matching legacy key, so a copied legacy object could reappear through fallback reads;
- detector and inspector adapters received the writable owning quarantine stream and could mutate or close bytes needed by the rest of the pipeline;
- the configured multipart limit bounded each section but not the complete authenticated request;
- objects without a usable stored filename did not receive an attachment disposition;
- the antiforgery opt-out was not documented as requiring explicit non-cookie authentication.

## Delivery

1. Delete both current and legacy representations and report success when either existed.
2. Flush quarantine writes and expose a fresh read-only, non-owning view to each adapter.
3. Add a bounded whole-request allowance for multipart framing while retaining the exact per-object limit.
4. Emit `Content-Disposition: attachment` for every download and add a filename only when one is available.
5. Consume Framework's structurally validated canonical media types.
6. Add focused regression tests and update operational guidance.

## Non-Goals

- product attachment records, retention, legal hold, sharing, quotas, or orphan cleanup;
- a module-owned database or object catalog;
- large/resumable upload protocols or inline/public delivery;
- cookie-authenticated browser forms. Hosts needing those own an antiforgery-protected endpoint over the application/storage contracts.

## Acceptance

- no legacy copy remains after delete when current and legacy keys both exist;
- adapters cannot mutate or own quarantine bytes and exact content still reaches storage;
- endpoint metadata bounds total request size and an object at the configured maximum remains valid;
- downloads without filenames remain forced attachments;
- focused and full Files gates pass with source, solution, boundary, and package checks.

## Evidence

- solution synchronization and a zero-warning standalone build passed;
- all 26 Files tests passed, including duplicate-key delete, quarantine ownership, maximum-size multipart, and unnamed-download regressions;
- reusable-module boundary, repository security, and repository release checks passed;
- the transitive package audit reported no vulnerable packages;
- the shared Framework gate passed all 1,109 tests and its package audit after media-type canonicalization changed.
