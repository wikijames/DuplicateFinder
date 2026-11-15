# Duplicate review workspace

## Scope and UI direction

This branch starts from `development` after the diagnostics and cleanup-safety merge. It implements the agreed combination: straightforward scan setup and a wide split-view results screen. The design is original Avalonia markup with a dark background, restrained teal accents, and standard window controls. Supplied screenshots informed the workflow, not copied branding or assets.

The initial view explains how to start a scan. Results use a group list on the left and file details on the right. The detail pane shows file names, full paths, sizes, and explicit keep/removal labels. The keep selector shows the filename first, with the containing folder and a full-path tooltip.

## Result-review behavior

- Every duplicate group can be selected; the old prototype exposed only the first group for cleanup.
- Keep choices are remembered while navigating groups within one scan.
- A new scan, edited scan setup, cancellation, failure, or completed cleanup invalidates the previous operation.
- Only the selected group's removal targets enter confirmation. Other groups' files are not included.
- Larger groups remain reviewable, but cleanup remains disabled above the existing 128-target limit or without a scan fingerprint.
- The existing scan-fingerprint, byte-comparison and Windows handle-based cleanup checks remain in force.

## Scan setup

All file types are scanned by default instead of the former `.txt`-only demo filter. Optional comma/semicolon/whitespace-separated extensions are normalized and deduplicated. Malformed extensions produce guidance without starting a scan. Image and video files participate in exact byte matching; this is not visual or perceptual similarity.

Browse selects a folder without automatically starting work, so filters can be reviewed first. Scanning still respects the core scanner's hidden-file and protected-directory defaults. Activity is shown with an indeterminate indicator; the UI does not claim an unmeasured percentage.

## Validation

- Release solution build: zero warnings or errors.
- Full Windows suite: 80 tests passed, including 16 new workspace/filter cases.
- Workspace tests cover group-scoped operations, remembered keep choices, scan resets, invalid group/file selections, deselection, missing fingerprints and oversized groups.
- Synthetic scanner tests cover the all-file default, normalized filters and invalid extension input.
- Running-window accessibility checks covered two distinct groups, scroll access to the third file in a group, changing and remembering a keep choice, confirmation containing only the selected group, Cancel without removal, filter changes invalidating old results, invalid input, empty results, missing roots and recovery.
- Cancellation was checked with two generated 128 MiB files; navigation and cleanup were disabled during scanning and controls recovered afterward.
- At the supported minimum window size, folder/filter inputs, group navigation, the keep selector, file list and removal action remained within the window bounds.
- No actual cleanup was invoked during the new UI checks. Existing integration tests remove only their own generated fixtures.

CI now runs for both `development` and `main`, matching the repository's development-branch PR workflow. The first hosted run of this branch remains pending publication.

## Remaining Windows release work

See the [Windows release checklist](windows-release-checklist.md). This branch does not add file-content thumbnails, multiple roots, drag/drop, bulk selection across groups, similarity analysis, quarantine, restore or recycle-bin execution. Broader DPI, high-contrast, screen-reader and clean-machine release testing remain required.

## Suggested PR

**Title:** Add a split-view duplicate review workspace

**Base:** `development`

**Summary:** Replace the first-group-only desktop prototype with group navigation and a selected-group file review. Preserve keep choices within each scan, scope cleanup to the selected group, and invalidate stale operations when setup changes. Scan all extensions by default with optional validated filters. Add an original dark Avalonia layout, Windows release checklist, and CI coverage for development-branch PRs.

**Validation:** Release build clean; 80 tests passed; running-UI checks completed with synthetic fixtures. Recoverable cleanup and broader Windows release gates remain documented follow-up work.
