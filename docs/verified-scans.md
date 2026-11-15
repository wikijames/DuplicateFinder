# Verified scans and completeness reporting

## Behavior

Exact-match scans now perform streaming byte comparison after size grouping, prefix hashing and full SHA-256 hashing. Files sharing a hash bucket are partitioned by actual byte equality, and every reported group retains its real content fingerprint for the existing guarded cleanup pipeline.

Verification opens both files with read sharing, checks sizes and modification timestamps against discovery metadata, reads with two 64 KiB buffers, and recomputes both full SHA-256 fingerprints. Changed, missing or unreadable candidates are excluded. Valid candidates remain eligible even if the original comparison reference fails. The new tests force distinct content into the same grouping bucket and verify that separate byte-identical groups survive without being merged.

Candidate metadata is grouped by size as discovery proceeds. File content is streamed; whole file contents are not retained. Metadata and candidate paths still consume memory proportional to the scan, so large-directory memory measurements and further tuning remain release work.

## File-system failures

Directory enumeration is handled at both enumerator creation and lazy `MoveNext` calls. Recoverable I/O, access and security errors are recorded without aborting healthy portions of the scan. Metadata, sampled/full hashing, and byte verification similarly isolate file failures. Cancellation remains an exception and never becomes a successful partial result.

Discovery explicitly applies hidden-file filtering rather than letting the enumeration defaults silently override `IncludeHidden`. System entries retain their previous default exclusion. Child reparse/link entries remain excluded by policy; an explicitly selected linked root is reported as an incomplete folder instead of being traversed. Reserved quarantine directories and configured exclusions remain excluded normally.

## API and UI

`DuplicateDetector.Scan` returns a `DuplicateDetectionResult` containing groups and an immutable `ScanIssueSummary`. It reports unique affected file/folder counts, with changed files also included in the skipped-file total. Internal path sets deduplicate failures; paths are not exposed in the issue summary or scan status text.

The existing `FindDuplicates` method remains a convenience returning only groups; callers needing completeness information should use `Scan`. `DuplicateScanCoordinator` carries the issue summary into its scan summary/report, including `IsComplete`.

A scan with encountered failures is labeled **Incomplete scan**, even when no duplicates were verified. It is never presented as the ordinary no-duplicates success message. The desktop retains valid results and shows a persistent warning banner with counts. Selecting another keep file or cancelling cleanup does not hide that warning. A new scan or edited setup clears the old result state, and a successful rescan clears the warning.

Verified groups from an incomplete scan remain reviewable. Quarantine/permanent deletion still require their existing confirmation, fresh fingerprints, byte comparison and Windows handle safeguards.

## Boundaries

Completeness means no unexpected failures were encountered within the configured scan scope. It is not a file-system snapshot or a guarantee that files cannot change after verification. Unique-size files are not fully read, deliberate exclusions are not errors, and issue counts describe detected failures rather than an exhaustive history of changes. Cleanup continues to revalidate current contents.

Hard-link-aware space accounting, the broader drive/long-path matrix, performance measurements, and remaining UI/release gates are tracked separately in the [Windows release checklist](windows-release-checklist.md). No personal folders or third-party tools are needed for the new tests.

## Validation and PR scope

- Release build: zero warnings/errors; full Windows suite: 129 passing tests.
- Synthetic tests cover forced hash-bucket collisions, distinct tails beyond the sampled prefix, same-size changes with restored timestamps, changed lengths, missing references/targets, failures after discovery, lazy enumeration/access failures, locked files, cancellation, empty files, hidden attributes, preserved healthy groups and sanitized incomplete reports.
- Running-UI checks locked one generated file while scanning: the remaining healthy pair stayed reviewable, the warning survived keep-file changes and Cancel, and releasing the lock followed by rescan restored the full group and cleared the warning. An incomplete scan with no verified groups also retained the warning and disabled cleanup. No cleanup was executed during these UI checks.

Suggested PR title: **Verify duplicate bytes and report incomplete scans**. Base: `development`. Independent image-similarity research edits are outside this feature's intended commit.
