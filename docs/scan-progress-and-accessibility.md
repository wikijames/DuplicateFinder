# Scan progress, appearance and keyboard controls

The portable scan API accepts an optional `IProgress<ScanProgress>` observer. Snapshots contain the current stage and cumulative counters, with no file paths. Existing source calls can omit the new optional argument.

## Counter definitions

- Discovered files: unique candidate paths yielded by filtered discovery, before metadata/identity checks. This includes candidates later skipped or excluded; it is not the number of successfully scanned files.
- Sampled files and hashed files: successful sample/full hash passes. A file can contribute to both counts.
- Verified pairs: completed byte/content comparisons, including unequal pairs. This is not a group count or a unique-file count.
- Bytes read: bytes consumed by sampling, full hashing and pair verification. Repeated reads count again; this is not unique data size or disk space saved.

Stages are Discovering, Sampling, Hashing, Verifying and Completed. Matching stages can repeat for each size bucket. Completed means processing returned, including scans with skipped-file warnings; it does not certify complete filesystem coverage. Cancelled/failed work does not report Completed. There is no estimated percentage, total work, or ETA.

Counters are reported at most about every 150 ms within a stage, plus stage transitions. Observers run synchronously on the scanning thread and should return quickly without throwing. The desktop observer retains one latest snapshot and samples it every 200 ms on the UI thread. It does not enqueue every update. Its timer stops on success, cancellation and failure, so late progress cannot replace the final status.

## Desktop behavior

- System appearance follows the OS preference; Light and Dark override it for this session. Custom surface, text, accent, warning and cleanup colors use theme resources. Standard controls retain Fluent styling. Appearance is not persisted and cannot change during an operation.
- Ctrl+F focuses and selects the results search text when no operation/modal dialog is active.
- Escape cancels an active scan. The Cancel button remains available, and cancellation clears results and cleanup eligibility.
- Search and cancellation tooltips explain the shortcuts. Appearance, progress and status have descriptive automation labels.
- Compact spacing and an inline preview action leave both result lists usable at the current 920×660 minimum size on the tested display configuration.

These changes are an accessibility improvement, not a completed accessibility audit. High contrast, screen-reader announcements, full tab order, other Windows configurations and 125–200% display scaling remain release gates. System appearance is not a claim of Windows high-contrast integration.

## Validation

Release build succeeds with zero warnings/errors; all 202 tests pass. Six new tests cover exact cumulative counters across repeated reads, empty scans, zero-byte duplicate comparisons, cancellation without completion, unchanged matching results, and a bounded latest-snapshot mailbox.

Running UI checks used generated fixtures only. They confirmed the populated layout at minimum size, System/Light/Dark selections, Ctrl+F focus, measured progress during a synthetic large-file scan, Escape cancellation, restored scan controls and disabled stale cleanup. Source fixtures were not cleaned up by the app. No personal directories were scanned.

This branch builds on the results-search-and-previews branch until that prerequisite is merged into development. Remaining performance, diagnostics, packaging and release checks are tracked in the [Windows release checklist](windows-release-checklist.md).
