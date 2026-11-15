# Guarded quarantine and restore

## User workflow

Quarantine is the default cleanup action. Scan and select a duplicate group, choose the keep file, then choose **Review removal**. Confirmation lists only that group's removal targets and lets you enter or browse for a destination folder. The default destination is the scan folder. Cancel remains the default confirmation action.

Quarantine moves files into:

```text
<chosen folder>/.DuplicateFinder-Quarantine/batch-<random batch identifier>/
    recovery.manifest
    <random stored identifier>.bin
```

Files are retained, so quarantine does **not** free disk space. The UI labels the result summary as bytes in extra copies rather than space already freed. Ordinary scans exclude the reserved `.DuplicateFinder-Quarantine` directory by default.

Scan setup also offers file-type presets: All files, Images, Audio, Videos, PDF, Documents, Archives, and Custom extensions. The included extension list is visible and read-only for presets; Custom allows editing and remembers its draft when switching categories. All files remains the default. An empty Custom entry is rejected rather than silently broadening the scan. Categories filter filename extensions only and continue using exact content matching, without decoding files or implying visual/audio similarity. Switching presets invalidates prior results and cleanup eligibility.

The selected keep file is not moved. Duplicate basenames are safe because each stored payload gets its own random name. Permanent deletion remains available as a separate, explicit action with an irreversible-deletion warning. Recycle-bin execution remains unsupported.

## Manifest and preflight

- Source files undergo the existing whole-group size, scan-fingerprint and byte-comparison checks before a batch is created.
- All source, payload and ancestor-directory handles remain protected through the move. Moves use `SetFileInformationByHandle` with replacement disabled; files are not reopened by path to move them.
- The immutable manifest records schema version, batch identifier, original paths, stored names, sizes and SHA-256 fingerprints for every planned target.
- The manifest is created exclusively and flushed to disk before the first move. It is retained after restore; no batch, payload, or manifest is automatically purged.
- Manifest metadata is protected with Windows DPAPI under the current user profile. Integrity failures and schema/path validation failures stop recovery. Payload contents themselves are not encrypted.
- Recovery is bounded to 128 entries, a 1 MiB protected manifest, and the existing 512 ancestor-directory handle limit.
- All source files and the destination must use the same local drive. Cross-volume copy-and-delete fallback is deliberately absent. Mount/reparse paths, UNC/device paths and unsupported drives are rejected.
- A quarantine operation cannot be passed to the permanent-delete service. The session also prevents deletion unless it was prepared for an explicit delete action.

## Restore and interruption recovery

Choose **Restore batch**, paste or browse to the batch folder, inspect the protected manifest, and confirm the original paths. This works after restarting the app; a scan and the original keep file are not required. The last batch path is remembered only within the current app session, so retain the batch location for later recovery.

Restore reloads the manifest and checks that its fingerprint matches the reviewed version. It validates and locks every entry before moving any payload:

- Stored payload present, original absent: verify payload size/hash, then restore through its open handle.
- Stored payload absent, original present: verify the original size/hash and count it as already at its original location.
- Both present: stop with a conflict, even if contents happen to match. Never overwrite or delete either file.
- Neither present, changed content, missing original parent, or inaccessible/linked path: stop for manual resolution.

The original parent folders must exist; restore does not automatically recreate directory trees. Native moves also refuse destinations created after preflight. Batch-wide moves are not a transaction: a failure can leave some files moved. The UI reports completed move counts, retains the batch location and asks the user to resolve the conflict and explicitly restore again. Repeated restore is harmless when all entries already match at their original locations.

The manifest describes the entire plan, so recovery does not depend on a final success marker or rewriting metadata after each move. Integration tests simulate interruptions after one quarantine move and one restore move. Machine power loss, storage/controller failures and disconnected physical drives still need broader release validation; the app does not promise transactional durability across such failures.

A destination or manifest-write failure before the first move can leave an empty batch folder or an incomplete manifest. No source files have moved at that stage; the app leaves these artifacts alone rather than automatically deleting them. Only batches with a valid protected manifest can be inspected for restore.

## Privacy and recovery boundaries

Original paths are necessary recovery metadata and remain in the local protected manifest. They are not included in trace messages or transmitted. DPAPI recovery requires the original Windows user profile and its protection keys; a same-named account after reinstall is not sufficient. This is not a portable backup format. Do not discard the manifest, user profile keys, or payloads while recovery is needed. The underlying retained files remain ordinary file contents with their existing Windows permissions.

There is no claim of protection against software running with the same user's authority, administrator/kernel access, hostile file-system drivers, pre-existing writable mappings, or manual edits/removal of batch data. The core library remains UI-agnostic; native rename and DPAPI calls live in the desktop project. No dependency package was added.

## Validation and PR scope

- Release build: zero warnings/errors; 109 tests passed on Windows, including 10 preset-filter cases.
- New tests cover quarantine/restore round trips, same-name targets, keep preservation, interrupted quarantine/restore, repeated restore, conflicts before and after preflight, changed/missing payloads, missing original parents, corrupt/replaced manifests, traversal entries, scan exclusion, action switching and accidental permanent-delete rejection.
- Running-UI checks with generated files confirmed quarantine as the default, confirmation and Cancel, preserving the keep file, creating both payloads and the manifest, a separate restore confirmation, Cancel restore, preserving an unrelated conflicting file, successful restore, and a harmless repeated restore. A relaunched app inspected the persisted batch without a scan and recognized its already-restored entries. The separate permanent-delete confirmation and recovery-control visibility at minimum window size were also checked.
- No personal directories were scanned and no personal files were moved or deleted. Tests use uniquely generated temporary fixtures.
- Running-UI preset checks covered Images/PDF filtering, read-only preset lists, editable and remembered custom input, stale-result invalidation, empty-custom rejection, the All files default, and control visibility at minimum size.

Suggested PR title: **Add guarded quarantine, batch restore, and file-type presets**. Base: `development`. The independent image-similarity research edits remain outside this feature's intended commit.

## API references

- [SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)
- [FILE_RENAME_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info)
- [CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [CryptUnprotectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptunprotectdata)
