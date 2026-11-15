# Diagnostics and cleanup safety

This records the diagnostics branch's original scope. The subsequent [guarded-quarantine feature](guarded-quarantine.md) adds default recoverable moves and explicit batch restore while retaining the checks below for permanent deletion.

## Behavior

Desktop scans run in the background. Cancel propagates to discovery and hashing; results from a cancelled scan are discarded. Scan, folder-selection and cleanup failures clear stale review state and report an operation name and exception type. Trace output excludes exception messages, stack traces and scanned paths. This branch does not introduce persistent diagnostic logs.

A cleanup review preserves the explicitly selected keep file and lists every removal target. Confirmation shows the same immutable operation that will execute, states that deletion is permanent, and defaults to Cancel. Scan, browse and review controls are disabled during cleanup; file validation runs off the UI thread. Success and failure both require a new scan before another cleanup.

## Preflight and execution

1. The detector records each duplicate group's full SHA-256 fingerprint and size. These values pass through the plan, execution request and operation.
2. Windows cleanup accepts ordinary absolute paths on local fixed or removable drives. Unsupported actions, overlapping paths, missing scan fingerprints and oversized groups stop without removal.
3. Parent directories are opened from the drive root downward without delete sharing. Reparse points are rejected using attributes from the open handles.
4. The keep file and all targets are opened before validation. Their sharing modes reject other writers and delete/rename access. The target handles have deletion access; the keep handle does not.
5. Each target is compared byte-for-byte with the keep file, using two 64 KiB buffers. Size and SHA-256 must also match the original scan. All comparisons must pass before deletion starts.
6. Files are deleted through those same validated handles. No file path is reopened for deletion. Handles remain open until cleanup finishes or stops.

The operation is limited to 128 removal targets and 512 distinct ancestor directory handles. All handles acquired during a failed preflight are released. These limits bound resource usage and intentionally reject larger groups rather than weakening validation.

## Boundaries

- Only Windows permanent deletion is implemented by the guarded execution path. Recycle-bin operations are rejected because the earlier shell call requires path-based access after releasing locks. Quarantine, backup and restore remain separate roadmap work.
- A scan fingerprint represents file contents, not historical file identity. An identical-content replacement before preflight can pass; the opened file must still match the reviewed content, and remains locked afterward.
- Multi-file deletion is not a transaction. A storage or permission failure during execution can leave earlier targets removed. The UI clears the operation and asks for a new scan; it never retries automatically.
- This is an ordinary desktop sharing/access model, not a defense against administrator/kernel access, hostile file-system drivers, or pre-existing writable memory mappings.
- Scan results remain hash-based. Byte comparison is mandatory at cleanup, but adding it to scan-time candidate classification is separate work.

## Validation

The Release solution build completed with no warnings or errors. The 64-test suite passes on Windows, including synthetic integration tests that exercise actual handle-based removal only inside uniquely generated temporary fixtures. Coverage includes changed and missing keep/target files, identical changes to all copies, failed lock acquisition and release, rename/write protection, the target-count limit, unsupported actions, and preserving a keep file sorted after its removal targets. Hash cancellation tests trigger cancellation from a read rather than relying on task scheduling or file size.

Accessibility-driven checks in the running desktop app confirmed scan cancellation and restored controls, three-copy keep selection, the confirmation count and exact target list, Cancel, rejection of a deliberately stale result while preserving all three synthetic files, and rejection of a generated junction parent while preserving its files. UI automation locates confirmation as an owned descendant window.

Windows CI restores .NET 10 dependencies, builds Release, and runs the tests on PRs to `main`, pushes to `main`, or manual dispatch. Its first hosted run remains unverified until publication is authorized.

## API references

- [CreateFileW access, sharing and reparse-point behavior](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
- [GetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandle)
- [SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)
- [FILE_DISPOSITION_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_disposition_info)
- [Checkout action](https://github.com/actions/checkout) and [setup-dotnet action](https://github.com/actions/setup-dotnet)
