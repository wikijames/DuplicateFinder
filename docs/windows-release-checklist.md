# Windows release checklist

This is the remaining work toward a Windows v1 for exact duplicate files. It is not an effort percentage or a promise that the wider roadmap is complete. Image similarity and Android are separate release scopes.

## Completed: duplicate review workspace

- [x] Start from merged `development`, with the diagnostics and guarded-cleanup work present.
- [x] Agree the UI direction: simple scan setup followed by split-view results.
- [x] Implement an original dark Avalonia layout with restrained teal accents, standard Windows window controls, and keyboard-accessible lists.
- [x] Make all duplicate groups selectable, with the selected group's paths, sizes, and keep/removal intent visible.
- [x] Preserve keep choices while navigating within one scan; reset them on a new scan.
- [x] Scope confirmation and cleanup to the selected group.
- [x] Replace the `.txt`-only demo scan with all file types by default and optional extension filtering.
- [x] Run CI for PRs and pushes to `development` as well as `main`.
- [x] Complete unit/integration tests, running-UI checks, and final diff review before requesting PR approval. See [branch validation and PR summary](duplicate-review-workspace.md).

## Recoverable cleanup

- [x] Implement same-drive quarantine with locked-handle validation and replacement protection.
- [x] Define destination selection, collision-safe names, protected manifest integrity, interruption recovery, and rejection of cross-volume moves.
- [x] Provide explicitly confirmed restore, conflict handling, original-path validation and safe repeat/resume behavior.
- [x] Report completed move counts and batch location during partial failures; never automatically retry or purge.
- [x] Test deterministic interruptions, stale/locked files, restore conflicts and manifest failures with synthetic fixtures. See [guarded quarantine](guarded-quarantine.md).
- [ ] Expand release testing for real permission configurations, disconnected drives, power loss and storage failures. Recycle-bin integration remains optional follow-up to the implemented quarantine path.

## Matching and file-system resilience

- [x] Add streaming scan-time byte verification after hashing; preserve fingerprints for guarded cleanup.
- [x] Isolate detected disappearing, changing and inaccessible files/directories; retain healthy groups and mark incomplete scans. See [verified scans](verified-scans.md).
- [x] Define conservative hard-link exclusion and independently block linked files in cleanup/restore. Validate NTFS identities, outside-root aliases, long-path round trips and rejected path forms. See [hard-link and path coverage](hard-link-safety.md).
- [ ] Expand real reparse/symlink, OS and drive coverage beyond the current NTFS fixtures, including ReFS/FAT/exFAT/removable media and shell long-path behavior.
- [x] Expose unique skipped-file/folder and changed-file counts with an immutable issue summary and persistent incomplete-scan warning.
- [ ] Measure memory retention on large candidate sets and bound work queues before adding concurrency.

## Finish Windows UI and accessibility

- [x] Refine the existing review hierarchy, keep-file presentation, theme/focus styles and scrollable confirmation dialogs without changing service contracts. See [scope and synthetic UI validation](windows-ui-refinement.md).
- [x] Refine local diagnostics status, privacy expanders and recording controls without changing the recorder or limits. See [UI behavior and actual validation coverage](opt-in-diagnostics.md#diagnostics-ui-refinement).

- [x] Add common file-type presets with visible extension lists and a custom-extension option, while retaining exact matching.

- [x] Add multiple scan roots and folder drag/drop, with duplicate/overlapping-root handling. Native Explorer drag/drop remains a manual release check; see [scan controls and validation](multiple-scan-folders.md).
- [x] Expose supported size, hidden-file and exclusion options with clear defaults.
- [x] Add group sorting/search while preserving whole groups and protected keep choices. Cleanup remains scoped to the reviewed group; optional individual removal selection is deferred.
- [x] Add bounded, read-only UTF-8 text previews; unsupported formats retain metadata-only details. See [results controls and preview limits](results-search-and-previews.md). PDF rendering remains a separate extension.
- [x] Add bounded, on-demand PNG/JPEG comparison of the protected keep file and one extra copy, with read-only navigation. See [image comparison and limits](image-comparison-previews.md). Other image formats, PDF rendering and visual-similarity detection remain separate.
- [x] Show measured scan stages and cumulative work counts without a completion percentage. See [progress and keyboard validation](scan-progress-and-accessibility.md).
- [ ] Complete keyboard navigation, screen-reader, high contrast and display scaling validation. Ctrl+F, Escape cancellation, automation labels and populated minimum-window layout have local checks; the wider matrix remains open.
- [x] Provide session-only System/Light/Dark appearance with theme-aware colors and verified selection controls.
- [ ] Validate rendered contrast and appearance across the Windows/display/accessibility matrix before release.

## Performance and diagnostics

- [x] Add a reproducible synthetic benchmark harness and validate all workloads locally; measure a targeted hashing-buffer allocation reduction. See [baseline, metrics and limitations](synthetic-scan-benchmarks.md).

- [ ] Benchmark reproducible synthetic data on SSD/HDD: many small files, large files, duplicate-heavy and mostly unique scans.
- [ ] Record throughput, allocations, peak memory and cancellation latency before tuning concurrency.
- [x] Add explicit opt-in, session-only persistent diagnostics with a fixed privacy schema, 16/64 KiB file caps, a bounded queue and user-managed retention. See [behavior and validation](opt-in-diagnostics.md).
- [x] Audit direct/transitive NuGet vulnerabilities and inventory license metadata/portable footprint. See [findings and scope](dependency-review.md).
- [ ] Close application-license choice, native/font attribution and bundled-runtime security review before public distribution.
- [ ] Expand automated and interactive tests for the supported Windows and file-system matrix.

## Packaging and release gate

- [x] Prepare a repeatable self-contained Windows x64 portable test candidate with notices, inventories, checksums and extraction checks. See [build and validation](windows-release-preparation.md). Installer support is deferred.
- [ ] Certify the minimum supported Windows build and final architecture/support matrix; Windows 11 x64 is the initial candidate target.
- [ ] Test a Release publish on a clean Windows environment, including a machine without the SDK.
- [ ] Validate dependencies, install/update/uninstall behavior, application identity and any signing requirements.
- [x] Document portable-candidate use, replacement/removal, cleanup/restore, diagnostics and known limitations in the [user guide](windows-portable-guide.md).
- [ ] Finalize public-release support, signing and acceptance documentation after the remaining gates pass.
- [ ] Verify hosted CI and review branch-protection settings.
- [ ] Complete the release safety review and acceptance checklist before tagging a Windows v1 release.

## Optional additions after the exact-file Windows baseline

- [ ] Resolve the image decoder/hash dependency decision, then implement opt-in image similarity with separately labeled review-only results.
- [ ] Evaluate video similarity separately; exact matching of video files already uses ordinary byte matching.
- [ ] Validate Android storage access and shared-core boundaries before defining an Android prototype or release.

## UI direction and provenance

Use the supplied screenshots as workflow references: straightforward scan setup, then enough horizontal space to inspect duplicate groups and file details. The implementation uses original Avalonia layouts, text and styling. It does not reuse another application's branding, icons, artwork or proprietary assets. File-details preview, paid upgrades, similarity modes and automatic selection are not implied by the current layout.

Each feature branch should implement and validate one coherent part of this checklist, then request review against `development`. Remaining release gates stay visible rather than being counted as complete because the screen looks finished.
