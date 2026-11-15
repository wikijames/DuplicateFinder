# Results search, sorting and text previews

Search matches a case-insensitive substring in any file path, including filenames. Results retain whole groups: all copies remain visible when a group is selected. The left panel reports visible groups separately from the scan totals above the workspace. Filtering does not change those totals or the incomplete-scan warning.

Sort groups by filename ascending, extra-copy bytes descending, individual file size descending, or copy count descending. Ties use the first file path for deterministic ordering. These controls change presentation only. Keep choices remain attached to the original groups for the current scan. If search hides the selected group, review and cleanup eligibility are cleared until a visible group is selected.

## Preview boundaries

Select a file in the group, then choose **Preview selected file**. Preview is on demand and read-only; it does not start an external application, render HTML, execute scripts, verify equality, or alter cleanup decisions.

Supported extensions are `.txt`, `.md`, `.csv`, `.json`, `.xml`, `.log`, `.cs`, `.css`, `.html`, `.yml` and `.yaml`. Content must be valid UTF-8, optionally with a UTF-8 BOM, and contain no control characters other than tabs and line endings. Empty text is supported. Unsupported formats and encodings remain metadata-only, including images, PDFs and office documents.

Preview accepts files up to 64 KiB and reads at most 65,537 bytes. Larger files are not partially decoded. Work runs off the UI thread, with cancellation on window close. Only one preview can be requested at a time; scan, review and cleanup controls are disabled while the preview is loading or its modal dialog is open.

The Windows preview reuses guarded local-path validation, ancestor-directory handles, and a read-only file handle. It rejects reparse points, hard-linked files, nonlocal/unsupported paths and incompatible file locks. Handles are disposed after the bounded read; no deletion access is requested. The preview displays current file content, which can differ from scan-time content. Cleanup still performs its own fingerprint/content validation. This is a Windows implementation; other platforms report preview as unavailable.

## Validation

- Release build: zero warnings and errors.
- Full suite: 196 passing tests, using generated fixtures.
- New tests cover whole-group filename/path search, no-match results, retained keep choices, all sort metrics, deterministic ties, UTF-8/BOM, size boundaries, binary/invalid encoding, missing/locked files, hard-link rejection, unchanged contents and cancellation.
- Running Windows UI checks covered visible counts, selecting filtered results, clearing cleanup eligibility when search hides a selection, restoring groups by clearing search, opening/closing the native preview dialog and unchanged synthetic file content.

Hosted CI, full keyboard/screen-reader checks, display scaling, minimum-window layout, broader storage coverage, and native Explorer folder drops remain release checks. Image/PDF rendering is not part of this phase. See the [release checklist](windows-release-checklist.md).
