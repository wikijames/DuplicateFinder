# Windows UI refinement

Refines the existing split-view Avalonia interface without replacing its architecture. Baseline independently verified on merged development (`649c869`): Release build with zero warnings/errors and 215 passing tests. The unrelated README/image-similarity edits were preserved outside this change.

## Presentation changes

- Shared theme-aware button, list-row, section and keyboard-focus styles apply to the main window and dialogs. No animation, image loading or UI package was added.
- Group rows keep their filename/count/size hierarchy and default ListBox virtualization. Splitter limits protect both panes; the minimum main-window size remains 920×660.
- An internal presentation row distinguishes **KEEP · Protected in this group** from **Removal candidate · Requires confirmation**. It uses existing scan metadata only. Public presentation types/contracts are unchanged.
- The keep dropdown remains the only way to change the group's one keep choice. Selecting a file row selects a preview, not a removal target. Full paths remain in accessibility text and are available in the read-only preview path field.
- The summary labels files as files in groups and sizes as extra-copy content. A tooltip explains that quarantine does not free disk space.
- Persistent coverage notices have a bordered warning area. Initial empty-state wording points to Add folders and Scan folders; no-match search guidance remains intact.
- Scan stage text is separated from measured work counts. The existing 200 ms update timer, latest-snapshot mechanism and indeterminate progress remain unchanged.
- Cleanup confirmation separates the action consequences, protected keep file, quarantine destination and removal paths. Restore confirmation uses the same visual sections. Body content scrolls while actions remain outside the scroll area.
- Settings and diagnostics use the shared scrollable dialog layout. Preview and folder-management dialogs have minimum sizes. Preview file paths can be read/copied from a bounded read-only field.

## Interaction/accessibility changes

Confirmation dialogs initially focus Cancel and retain Cancel as the default. Dialog close returns focus to the relevant invoking control after busy controls are enabled. These changes do not change confirmation results, target sets, eligibility or operation execution.

Final status and validation messages configure polite accessibility live settings. In-progress scan text has live announcements disabled; frequent counts are not a live region. Actual screen-reader announcement delivery still requires Narrator validation and is not claimed as certified.

Eight explicit foreground/background resource pairs in each Light/Dark dictionary were calculated: normal text, muted text, selected-row muted text, keep badge, primary action, destructive action, warning text and focus against selection. All 16 pairs exceed 4.5:1 (lowest 5.14:1). This checks resource colors, not rendered hover/pressed/disabled states, system high contrast, font rasterization or full accessibility conformance.

## Validation

Release build: zero warnings/errors. All 215 existing automated tests pass; no tests were removed or weakened. No Core, hashing, byte-verification, quarantine, restore, native cleanup or diagnostics-recorder implementation was changed.

The opt-in [Windows UI smoke test](../tools/Test-WindowsReviewUi.ps1) runs a separate Release executable with generated fixtures only:

```powershell
dotnet build DuplicateFinder.slnx -c Release
powershell -NoProfile -File tools/Test-WindowsReviewUi.ps1
```

It checks three-copy matching, a non-default keep choice, preview without changing the keep choice, safe cancellation of quarantine and permanent-delete confirmations, initial/return focus, 480×360 dialog actions, a populated 920×660 main window, invalid settings cancellation, diagnostics remaining off, search clearing hidden cleanup eligibility, keep-choice retention after filtering, Light/Dark/System selection, Ctrl+F and Tab/Shift+Tab. It then scans 301 generated groups and checks that a persistent hard-link notice leaves review controls usable. It never confirms a cleanup or starts diagnostics and deliberately retains its temporary fixtures for inspection.

The smoke test passed locally. It is not a performance benchmark or a complete keyboard/accessibility audit and requires an interactive Windows session. CI retains the existing build/test checks.

A self-contained Windows x64 UI test ZIP was also published locally. One initial extracted-package scan reported a sanitized IOException before accepting the synthetic folder. Explicit Add followed by Scan succeeded, and three fresh extracted-package launches then passed direct scanning and confirmation cancellation. The initial failure was not reproduced or diagnosed; retain it as a release-testing follow-up rather than claiming a fix. No scan or filesystem service was changed to bypass the failure.

## Remaining validation

Narrator speech and live updates, Windows high contrast, full keyboard traversal, 125–200% display scaling, rendered interaction-state contrast and the broader long-path/storage matrix remain release gates. Large-result search remains synchronous; debounce or item-reuse optimization is deferred until UI measurements justify changing interaction timing. No individual removal selection, thumbnails, additional file metadata, similarity features or automatic keep selection were introduced.
