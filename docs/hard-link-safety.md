# Hard-link policy and Windows path coverage

## Policy

Windows desktop scans exclude every encountered file name whose physical file has more than one hard link. Such names share file storage, and deleting or moving one name does not establish that the file's data can be reclaimed. This conservative policy does not pick a preferred alias or offer linked names as keep/removal targets.

Only independent, single-link files contribute to duplicate groups and extra-copy totals. For example, a file with an alias plus two independent byte-identical copies produces a review group containing only the two independent copies. A linked file plus one independent copy produces no actionable duplicate group under this policy. Links outside the scan root still protect the in-scope name without requiring enumeration outside the selected folder.

The report includes `ExcludedHardLinkPathCount`: encountered names excluded by policy, not the total number of links on disk or the number of underlying physical files. Intentional exclusions do not make a scan incomplete. The desktop keeps an exclusion notice visible, including for alias-only scans and while cancelling cleanup. Unsupported identity reads remain ordinary skipped-file failures and make the scan incomplete.

Extra-copy bytes are still logical file lengths, not a measured allocation or a promise of physical space freed. Sparse files, compression, block sharing and later changes can affect actual disk usage. Quarantine retains disk usage.

## Architecture and scan checks

The portable core defines `IFileIdentityProvider`, accepting a caller-owned open `FileStream`, and an immutable identity value containing volume identity, a 128-bit file identifier, and link count. It does not call Windows APIs or reference the desktop project.

The Windows desktop supplies `WindowsFileIdentityProvider`. It queries `FileIdInfo` and `FileStandardInfo` through `GetFileInformationByHandleEx`. The 128-bit identifier avoids relying on the legacy 64-bit identifier for file systems such as ReFS. Unknown identity, zero links, pending deletion and unsuitable entries are rejected rather than treated as independent files.

Single-link identities are captured during discovery and rechecked against the actual open streams used for sampled/full hashing and byte verification. A same-content replacement with the old size and timestamp is still detected when the identity changes. The parameterless core detector remains available for portable byte matching; callers requiring this hard-link policy must supply an identity provider. The Windows desktop always does so.

This remains a scan over a changing file system, not a snapshot. File IDs are not permanent identifiers across time, and cleanup still revalidates current contents and handles.

## Cleanup and restore

The native cleanup session rejects files with a link count other than one, independently of how the scan was produced. The guard applies to the keep file, removal targets, quarantined payloads and already-restored originals. Move/delete targets are checked again immediately before action. An alias added after scanning cannot silently turn into an ordinary cleanup target.

Recovery manifests are unchanged. If a retained payload has acquired a hard link, restore stops before moving any files during preflight. The user must resolve the extra alias explicitly; the app never searches for or deletes aliases elsewhere.

## Windows paths

- Ordinary absolute local paths remain the public input format. Relative, UNC, raw device/extended-namespace inputs, alternate data streams and trailing-dot/space aliases are rejected by cleanup path validation.
- Trailing-dot and trailing-space components are checked before normalization because Windows normalization can erase them.
- Validated long paths are converted internally to the extended-length form only at native open/rename calls. They are not stored in manifests in device-path form.
- The Windows application manifest declares long-path awareness. No registry settings are changed.
- Same-drive quarantine and supported fixed/removable-drive restrictions remain in force; there is no cross-volume copy/delete fallback.

## Validation

Release build: zero warnings/errors. Full Windows suite: **149 passing tests**, including 20 new cases.

Generated NTFS fixtures cover physical identity across aliases, independent copies, aliases inside and outside the scan root, alias-only scans, accurate extra-copy totals under the exclusion policy, same-content identity replacement, identity-read failure, aliases added after scanning, and hard-linked quarantine payload rejection/recovery. Long ordinary paths over 260 characters complete scan/quarantine/restore and guarded permanent-deletion round trips. Rejected-path tests cover relative, UNC, raw namespace, stream and trailing-dot/space forms without accessing external locations.

Running-UI checks confirmed the exclusion notice, independent keep choices, logical extra-copy totals, the notice surviving Cancel, an outside-root alias affecting only the encountered path count, cleanup disabled when only one eligible file remains, and successful rescan after manually removing generated aliases. No cleanup was executed during these UI checks.

Only synthetic files and links were used. Other file systems, physical removable drives, disconnected drives, shell behavior with long paths, maximum path lengths and the wider OS/drive matrix remain release-validation work. The current NTFS results are not a ReFS/FAT/exFAT certification.

## Suggested PR

**Title:** Exclude hard-linked files and validate Windows long paths

**Base:** `development`

**Summary:** Add an injectable file-identity provider to portable scanning and a Windows implementation using open file handles. Exclude multiply-linked names from matching and extra-copy totals with a visible count. Detect identity changes during scanning and independently reject linked files during cleanup/restore. Support validated long native paths, reject ambiguous path aliases before normalization, and add synthetic hard-link/path integration tests. No package dependency was added.

The unrelated image-similarity research edits are excluded from this branch's intended commit.

## References

- [FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info)
- [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info)
- [GetFileInformationByHandleEx](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex)
- [Legacy file IDs and ReFS limitations](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information)
