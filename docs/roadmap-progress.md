# Roadmap progress review

See the [Windows release checklist](windows-release-checklist.md) for the current Windows v1 work list and the agreed setup-plus-split-view UI direction.

Reviewed against the local source on 15 November 2026. This is an evidence-based checklist, not a percentage of implementation effort or a release certification.

| Phase | Confirmed locally | Remaining or unverified |
| --- | --- | --- |
| Foundation | Solution, core and desktop projects, xUnit tests, repository rules; Windows CI workflow now prepared locally | First hosted CI run and remote branch protection are unverified |
| File discovery | Recursive enumeration, filters, cancellation checks, hidden-entry handling and Windows reparse-point exclusion | SSD/HDD benchmarks, file-system race handling and portable symlink coverage need review |
| Exact duplicates | Size/hash/byte verification, per-scan issue counts, Windows hard-link exclusion with identity rechecks, and guarded cleanup | Throughput/memory benchmarks and broader file-system validation remain |
| Desktop workflow | Background scans/cancellation, extension filters, split-view review, scoped confirmation, default guarded quarantine with batch restore, and explicit Windows permanent deletion | Recycle-bin execution is disabled; safe content previews and broader release validation remain |
| Image/media similarity | Evaluation and exact/similar result boundary documented | Decoder/hash dependency decision blocks implementation; video analysis remains separate |
| Release readiness | Sanitized scan/folder/cleanup failure reporting; diagnostics and cleanup-safety branch validated locally | Persistent diagnostics, profiling, release documentation, wider security review and quality gates remain |
| Android feasibility | Portable core is the architectural target | Storage-access review, boundary validation and prototype criteria remain |

The earlier estimate of 56% overall gave equal weight to roadmap items and relied on the README for desktop completion. It should not be treated as verified completion: broader file-system coverage, performance measurements and release validation still have gaps.

## Next small steps

Local UI checks confirmed cancellation during a synthetic scan, restoration of controls afterward, changing the keep selection in a three-copy group, confirmation and Cancel, and stale-result rejection. The desktop creates its plan directly from the review selection, preserving the chosen keep file and including all displayed removal targets.

Scans now byte-verify hash candidates and report encountered file/folder failures without discarding healthy groups or claiming complete success. The UI retains a persistent incomplete-scan warning. Cleanup still revalidates scan fingerprints and bytes through protected Windows handles. The 129-test suite uses generated fixtures; no personal files are used. See [verified scans](verified-scans.md) and [guarded quarantine](guarded-quarantine.md) for behavior and remaining release boundaries.

1. Review and publish the hard-link-safety branch; verify hosted CI against `development`. See the [branch summary](hard-link-safety.md).
2. Complete multi-folder scan controls, drag/drop, size and exclusion settings.
3. Complete scan progress, previews and accessibility; measure synthetic benchmarks before tuning performance.
4. Continue release diagnostics, storage-failure testing, packaging and security review without treating a feature branch as a full release certification.

Image similarity remains blocked on the dependency decision described in [the feasibility note](image-media-similarity-feasibility.md).
