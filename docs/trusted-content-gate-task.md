# Trusted Content Gate Task

Status: implemented; downstream publication verification in progress
Date: 2026-07-21

## Goal

Make the reusable Files upload front door fail closed in production by trusting inspected content rather than the caller-declared media type, without moving scanner implementations, product document lifecycles, or product-specific policy into GMA.

## Audit Finding

Files currently checks the caller-declared content type against the shared storage allowlist and can optionally scan a bounded temporary copy before storage. This prevents storage after a rejecting scanner response, but it leaves four production gaps:

- the declared media type is not verified from bytes;
- no provider-neutral content-type detector contract exists;
- required inspection can use the built-in unavailable fallback until the first upload instead of failing startup;
- production can compose the generic upload front door with inspection disabled or an empty allowlist.

## Ownership Boundary

- Framework owns provider-neutral detector, inspector-readiness, and detector-readiness contracts plus validated result value objects.
- Files owns its upload policy, temporary pre-storage quarantine, trusted-type enforcement, error mapping, metadata, and production startup validation.
- storage adapters own byte persistence only; direct Framework storage consumers do not inherit Files upload policy.
- hosts own concrete detector/scanner adapters, credentials, availability monitoring, edge limits, and the decision to compose Files at all.
- product modules own file purpose, business references, permissions, quotas, retention, legal holds, sharing, deletion evidence, and public delivery.
- cross-module workflows belong in an explicit extension, not in Files or Framework.

## Configuration

Move Files-front-door behavior out of shared `FileManagement` storage options and into module-owned options:

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

`FileManagement:AllowedContentTypes` remains a host-wide storage allowlist. In production, Files additionally requires that list to be non-empty. Development and test retain explicit opt-out behavior for local examples and compatibility.

## Upload Pipeline

1. Validate subject/scope, readable content, positive declared length, configured maximum, and declared media-type syntax.
2. If trusted detection or inspection is required, copy the body once into a bounded delete-on-close temporary quarantine file and verify the exact declared length.
3. Ask the configured detector for a canonical content-derived media type. Unavailable detection fails as a service dependency; unrecognized content fails as unsupported media.
4. Apply the allowlist to the trusted detected type, never to the caller declaration when trusted detection is enabled.
5. Rewind the same quarantine stream and ask the inspector to classify it. Only `Clean` may continue.
6. Store exact quarantined bytes under the existing private key using the trusted type and record bounded detector/inspector identities as metadata.
7. Dispose the quarantine file on every success, rejection, cancellation, mismatch, provider failure, or storage failure.

If trusted detection is disabled outside production, preserve the existing declared-type behavior. A mismatch cannot bypass policy when trusted detection is enabled: the detected type controls allowlisting and stored metadata.

## Startup And Readiness

- when Files is enabled in Production, require trusted detection, content inspection, and a non-empty allowlist;
- when a capability is required in any environment, require its readiness contract to report ready before the host starts;
- built-in unavailable implementations satisfy dependency injection but report unavailable and therefore cannot accidentally pass startup;
- readiness results expose only a normalized provider identity and state, not endpoint, credentials, exception text, or scanned content;
- request-time unavailable outcomes remain fail closed for a provider that becomes unavailable after startup.

## Compatibility

- existing `IFileContentInspector` implementations remain source compatible; readiness is a companion interface;
- trusted detection is opt-in outside Production;
- legacy objects and current storage keys remain unchanged;
- no migration or persistent catalog is introduced;
- direct `IFileStorage` consumers keep their current behavior.

## Acceptance Checks

- a spoofed declaration cannot make disallowed detected content pass;
- successful detection normalizes the stored/returned media type and records detector identity;
- unknown and unavailable detection produce distinct fail-closed errors and store nothing;
- rejecting or unavailable inspection stores nothing;
- detection and inspection see the exact submitted bytes from one bounded quarantine copy;
- length mismatch, oversize, cancellation, and provider exceptions leave no object or temporary artifact owned by Files;
- Production startup fails when either policy switch is off, the allowlist is empty, or either required provider is unavailable;
- Test/Development compatibility remains explicit and covered;
- Framework and Files standalone builds, tests, boundaries, package audits, and Windows/Linux CI pass;
- Skeleton and at least one downstream application verify against exact published Framework and Files commits.

## Non-Goals And Deferred Work

- choosing or embedding a MIME-detection or malware-scanning vendor/library;
- claiming EICAR, archive-bomb, or polyglot coverage without a concrete adapter and its conformance suite;
- persistent quarantine, operator review, repair, release, or legal hold;
- generic guest attachments or a document repository;
- large/resumable uploads, presigned URLs, public/inline delivery, thumbnails, or media processing;
- product-specific country, purpose, retention, or deletion policy.

## Verification Evidence

- Framework commit `f33353743029320d042465e596e9d4422d1d144c` passed GitHub Actions run `29789424685`.
- Framework locally built with zero warnings, passed 1,007 tests, and reported no vulnerable packages.
- Files locally built with zero warnings, passed its module-boundary guard and all 22 tests, and reported no vulnerable packages.
- Exact Files, Skeleton, and downstream application commits are recorded by the publication chain once their CI runs complete.
