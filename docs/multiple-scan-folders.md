# Multiple scan folders and settings

Scan several folders together to find exact duplicates across their boundaries. Add a typed path, select multiple folders with Browse, or drop local folders onto the scan setup panel. Adding folders does not start a scan or change files. The Folders button opens the selection manager; removing or clearing entries only changes the scan selection.

## Scope and defaults

- Up to 64 effective roots are accepted. Canonical duplicate paths and parent/child overlaps collapse into one root, with filters applying throughout that root. Selecting an excluded child separately does not override its exclusion when its parent is also selected.
- Windows path comparisons ignore case; other platforms use ordinal comparisons. Repeated discovered paths and available physical file identities are deduplicated. Broader case-sensitive Windows and path-alias coverage remains release work.
- Desktop roots must be existing local folders. UNC/device paths and quarantine folders are rejected. Invalid batches leave the previous selection unchanged.
- All roots share one matching pipeline and the selected extension preset. An initially missing root rejects the request. A root that disappears during enumeration produces an incomplete-scan warning while healthy results remain available.
- Settings support optional minimum/maximum sizes in MiB, hidden entries, and additional excluded subfolder names. Defaults have no size bounds and omit hidden entries. Protected defaults and quarantine exclusions remain active.
- Decimal sizes use the current culture. Minimum sizes round up to whole bytes and maximum sizes round down. Exclusions accept names separated by commas, semicolons or newlines; paths and wildcards are rejected.
- Settings are applied only after validation. Cancel preserves the previous settings. Reset defaults edits the dialog until Apply is pressed. Settings and folder selections are session-only.
- Changing the selection or applying settings invalidates prior results, including empty results and warnings. No automatic scan or cleanup occurs.

Cross-drive duplicates can be reviewed. Quarantine remains restricted to groups whose keep file and removal targets share a drive; these groups show a review-only explanation when that restriction prevents quarantine. There is no automatic switch to permanent deletion. The default quarantine destination is beside the selected keep file, and existing native cleanup guards still apply.

The portable core adds enumerable-root scan/report overloads. Existing single-root methods remain available. `DuplicateScanSummary.RootPaths` contains the normalized read-only roots; `RootPath` retains the first root for existing callers.

## Validation

Release build succeeds with no warnings or errors; all 179 tests pass. New synthetic tests cover cross-root matching, overlaps, root limits, disappearing roots, repeated discovery paths, selection validation, size boundaries, exclusions, unsupported drop payloads and cross-drive review behavior.

Running Windows UI checks covered adding and deduplicating folders, cross-folder matching, selection removal and clearing without deleting fixture files, result invalidation, invalid size ranges, Apply/Cancel/Reset behavior, hidden files and excluded subfolders. Native Explorer drag/drop and the full accessibility/display-scale matrix still require manual release validation; payload validation tests do not substitute for native drag/drop coverage.

This phase does not certify large-dataset performance, all storage formats, snapshots of changing folders, or release readiness. See the [Windows release checklist](windows-release-checklist.md).
