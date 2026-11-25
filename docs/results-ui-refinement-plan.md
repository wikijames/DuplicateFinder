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
