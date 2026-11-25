# Results UI refinement

Deliver the approved refinements in two UI pull requests. The existing drive-root
scan fix must merge first. Keep scanning, hashing, byte verification, quarantine,
restore and cleanup guarantees unchanged. Retain exactly one kept file per group;
preview activation never changes that choice or confirms removal.

## PR 1: Compact setup and direct image previews

- Commit 1: place folder entry, file type and scan actions together on wide
  windows; wrap into two rows at compact sizes. Keep selected locations visible.
  Show editable extensions only for Custom and describe preset extensions in a tooltip.
- Commit 2: make supported image names in both results panels open the existing
  comparison window. Keep ordinary group navigation and row selection available.
  Provide keyboard activation, clear link styling and focus restoration.
- Validate Release build and all existing tests. Exercise generated image fixtures,
  light/dark/system themes, compact/wide layouts, custom-filter retention, image
  navigation and unchanged keep choices. Commit and push, then provide PR details.

## PR 2: Continuous grouped results and lazy thumbnails

- Commit 1: replace the single-group detail area with a continuous virtualized
  list of group headers and file rows. Keep search/sort and make sidebar navigation
  scroll to the corresponding group. Preserve each group's keep choice.
- Commit 2: add small on-demand thumbnails with bounded decoding, caching,
  disposal and cancellation as rows leave view; retain fallbacks for unsupported,
  oversized, damaged or inaccessible images. Do not decode all scan results eagerly.
- Commit 3: verify keyboard navigation, focus, themes, responsive layout and large
  synthetic result sets. Keep cleanup explicitly scoped to the active group; do
  not introduce batch removal or individual removal selection.
- Validate build/tests and synthetic UI acceptance before committing/pushing the
  completed branch and supplying PR details.

The image viewer remains internal and read-only, supporting existing bounded
PNG/JPEG previews. No default Windows viewer launches, new decoder packages,
similarity matching or new cleanup modes belong in these PRs. Elapsed-time/ETA
enhancements and extension-filter ordering are separate follow-ups.

## PR 1 validation and handoff

- Release build: zero warnings/errors; all 245 automated tests pass.
- Targeted native UI checks used a generated fixture with two PNG duplicate
  groups and one text group. File/group image links, Enter activation, Escape,
  focus restoration, extra-copy navigation and a nondefault keep choice passed.
- Inspected single-row maximized and two-row restored layouts, light/dark/system
  themes, conditional Custom extensions and retention when changing presets.
  Generated source hashes were unchanged. No cleanup was executed.
- Updated the existing synthetic review runner to activate image links and check
  focus return. The complete runner was not rerun during these targeted checks.
  Exact minimum-size, Narrator, high contrast and display-scaling acceptance
  remain separate checks; the restored-size check does not certify them.
- PR 1 and its prerequisite drive-root fix merged into development as `eeb717c`.
  The completed PR 2 implementation and its validation are recorded below.

## PR 2 implementation and validation

- Based on merged development `eeb717c`; grouped-list and thumbnail changes are
  separate commits on `feature/grouped-results-and-thumbnails`.
- All matching group headers and file rows share one flat virtualized list.
  Sidebar navigation scrolls to the corresponding group. Search/sort retain
  keep choices; hiding the active group disables cleanup until another is chosen.
- Per-row keep actions update exactly one group's presentation in place. The
  existing reviewed cleanup operation still applies only to the active group.
  Image names and thumbnails activate the existing internal comparison window.
- Thumbnails use the existing bounded PNG/JPEG reader, one decoder, a 64-entry
  encoded cache and 96-pixel output. Recycled rows cancel loads and dispose
  bitmaps. New scans clear the cache; operations pause readers and wait for idle
  before cleanup/restore review. No scanning or cleanup service was changed.
- Release build has zero warnings/errors and all 253 tests pass. Added tests
  cover a 10,000-group projection, unchanged active cleanup, per-group keep
  retention, serialized/coalesced thumbnail requests, bounded cache eviction,
  cancelled queued loads, stale-result rejection and 96-pixel output.
- Targeted native acceptance used generated files only: a three-group image/text
  fixture and a 1,002-group/2,004-file fixture. Observed bounded realized rows,
  keyboard End navigation to the final group, visible thumbnails, cross-group
  preview activation, focus return, per-row keep isolation, search retention,
  hidden-group cleanup disabling, single-group confirmation and cancellation.
  No cleanup was confirmed; source hashes remained unchanged in the small fixture.
- Inspected restored/maximized layouts and light/system-dark presentation.
  Exact minimum-size, Narrator, high contrast, DPI and clean-machine acceptance
  are not certified by these targeted checks. Updated synthetic review tooling
  counts file totals independently of virtualized controls; the full legacy UI
  runner was not executed as part of these targeted checks.
