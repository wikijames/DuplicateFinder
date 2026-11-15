# Windows release checklist

This is the remaining work toward a Windows v1 for exact duplicate files. It is not an effort percentage or a promise that the wider roadmap is complete. Image similarity and Android are separate release scopes.

## Current branch: duplicate review workspace

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

- [ ] Implement quarantine or recycle-bin support with equivalent validation and replacement protection.
- [ ] For quarantine, define the destination, collision-safe names, manifest integrity, interrupted-operation recovery, and cross-volume policy.
- [ ] Provide restore, including conflict handling and validation of original locations.
- [ ] Show what completed and what failed during partial cleanup; never silently retry or automatically remove files.
- [ ] Test permission failures, locked files, disconnected drives and interrupted operations with synthetic fixtures.

## Matching and file-system resilience

- [ ] Add scan-time byte verification after hashing; cleanup already requires byte comparison and scan fingerprints.
- [ ] Handle disappearing, changing and inaccessible files/directories without presenting an incomplete scan as a complete success.
- [ ] Define hard-link handling and test reparse points, symlinks, long paths and supported drive types.
- [ ] Expose per-file failures or skipped counts with clear scan-completeness information.
- [ ] Measure memory retention on large candidate sets and bound work queues before adding concurrency.

## Finish Windows UI and accessibility

- [ ] Add multiple scan roots and folder drag/drop, with duplicate/overlapping-root handling.
- [ ] Expose supported size, hidden-file and exclusion options with clear defaults.
- [ ] Add group sorting/search and selective removal if needed; keep one file protected in every reviewed group.
- [ ] Add a bounded, safe preview implementation for supported formats; unsupported files retain metadata-only details.
- [ ] Show measured scan progress/stages and counts without inventing a completion percentage.
- [ ] Test keyboard navigation, screen-reader labels, high contrast, display scaling, window resizing and long paths.
- [ ] Decide and validate system/light/dark appearance before release. The current branch implements the selected dark direction.

## Performance and diagnostics

- [ ] Benchmark reproducible synthetic data on SSD/HDD: many small files, large files, duplicate-heavy and mostly unique scans.
- [ ] Record throughput, allocations, peak memory and cancellation latency before tuning concurrency.
- [ ] Add useful opt-in/persistent diagnostics with privacy controls and explicit retention; current trace output is sanitized but not persisted.
- [ ] Review all direct/transitive package vulnerabilities, licensing and deployment footprint.
- [ ] Expand automated and interactive tests for the supported Windows and file-system matrix.

## Packaging and release gate

- [ ] Choose portable distribution, installer, or both; define minimum supported Windows version and architecture.
- [ ] Test a Release publish on a clean Windows environment, including a machine without the SDK.
- [ ] Validate dependencies, install/update/uninstall behavior, application identity and any signing requirements.
- [ ] Document safe use, known limitations, cleanup/restore behavior, diagnostics and recovery.
- [ ] Verify hosted CI and review branch-protection settings.
- [ ] Complete the release safety review and acceptance checklist before tagging a Windows v1 release.

## Optional additions after the exact-file Windows baseline

- [ ] Resolve the image decoder/hash dependency decision, then implement opt-in image similarity with separately labeled review-only results.
- [ ] Evaluate video similarity separately; exact matching of video files already uses ordinary byte matching.
- [ ] Validate Android storage access and shared-core boundaries before defining an Android prototype or release.

## UI direction and provenance

Use the supplied screenshots as workflow references: straightforward scan setup, then enough horizontal space to inspect duplicate groups and file details. The implementation uses original Avalonia layouts, text and styling. It does not reuse another application's branding, icons, artwork or proprietary assets. File-details preview, drag/drop, paid upgrades, similarity modes and automatic selection are not implied by the current layout.

Each feature branch should implement and validate one coherent part of this checklist, then request review against `development`. Remaining release gates stay visible rather than being counted as complete because the screen looks finished.
