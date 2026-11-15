# Roadmap progress review

See the [Windows release checklist](windows-release-checklist.md) for the current Windows v1 work list and the agreed setup-plus-split-view UI direction.

Reviewed against the local source on 15 November 2026. This is an evidence-based checklist, not a percentage of implementation effort or a release certification.

| Phase | Confirmed locally | Remaining or unverified |
| --- | --- | --- |
| Foundation | Solution, core and desktop projects, xUnit tests, repository rules; Windows CI workflow now prepared locally | First hosted CI run and remote branch protection are unverified |
| File discovery | Recursive enumeration, filters, cancellation checks, hidden-entry handling and Windows reparse-point exclusion | SSD/HDD benchmarks, file-system race handling and portable symlink coverage need review |
| Exact duplicates | Size grouping, sampled and full SHA-256 hashing, grouped results and tests; cleanup performs byte comparison against scan fingerprints | Scan-time byte comparison, throughput and memory benchmarks remain |
| Desktop workflow | Background scans/cancellation, all-file scanning with optional extension filters, split-view group navigation, remembered keep selection, scoped confirmation and guarded Windows permanent deletion | Recycle-bin execution is disabled; quarantine/backup, safe content previews and broader release validation remain |
| Image/media similarity | Evaluation and exact/similar result boundary documented | Decoder/hash dependency decision blocks implementation; video analysis remains separate |
| Release readiness | Sanitized scan/folder/cleanup failure reporting; diagnostics and cleanup-safety branch validated locally | Persistent diagnostics, profiling, release documentation, wider security review and quality gates remain |
| Android feasibility | Portable core is the architectural target | Storage-access review, boundary validation and prototype criteria remain |

The earlier estimate of 56% overall gave equal weight to roadmap items and relied on the README for desktop completion. It should not be treated as verified completion: hashing verification, quarantine/backup and release validation still have gaps.

## Next small steps

Local UI checks confirmed cancellation during a synthetic scan, restoration of controls afterward, changing the keep selection in a three-copy group, confirmation and Cancel, and stale-result rejection. The desktop creates its plan directly from the review selection, preserving the chosen keep file and including all displayed removal targets.

Cleanup uses scan-time size/SHA-256 fingerprints and bounded-memory byte comparison before removal. Missing, differing, linked or locked files stop the operation. Windows file and ancestor-directory handles are retained through handle-based deletion; stale review state is cleared afterward. The 64 tests include actual removal of generated temporary fixtures, with no personal files used. See [diagnostics and cleanup safety](diagnostics-and-cleanup-safety.md) for the supported path/action limits and remaining release boundaries.

1. Review and publish the duplicate-review workspace branch after approval; verify its first hosted CI run against `development`. See the [branch summary](duplicate-review-workspace.md).
2. Add recycle-bin or quarantine support that preserves the cleanup-safety guarantees.
3. Add scan-time byte verification and reproducible synthetic benchmarks before tuning performance.
4. Continue release diagnostics and security review without treating this branch as a full release certification.

Image similarity remains blocked on the dependency decision described in [the feasibility note](image-media-similarity-feasibility.md).
