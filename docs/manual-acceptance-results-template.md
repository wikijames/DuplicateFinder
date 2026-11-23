# DuplicateFinder manual acceptance record

Copy this file **outside the extracted application folder** before filling it in. Keep the packaged original unchanged so ZIP/file-manifest verification still works. Use generated or copied test files only. Record **Pass**, **Fail**, or **Not tested** for every row; a blank or unavailable check is not a pass.

Keep completed records in your private testing notes, outside tracked source files, and review them before sharing.

- Candidate source commit: copy from `package-info.json`
- Candidate ZIP SHA-256: copy from its `.sha256` sidecar
- Test date:
- Windows version/build:
- Display scaling offered/tested:
- App appearance tested: System / Light / Dark
- Contrast theme tested, if any:

Do not record account/machine names, personal paths or document contents.

## Hands-on checks on the development laptop

| Check | Expected outcome | Result | Observation using synthetic data |
| --- | --- | --- | --- |
| Ordinary launch and scan | Extract all files; launch the executable; scan a dedicated generated folder; results and any warnings are clear | Not tested | |
| Keyboard-only review | Reach folders, presets, scan/cancel, results, keep selection, previews and recovery using Tab/Shift+Tab and standard keys; focus remains visible | Not tested | |
| Keep-file clarity | Exactly one protected keep choice is clear; selecting an image/text preview does not change it | Not tested | |
| Cleanup cancellation | Cancel review and verify generated sources remain unchanged | Not tested | |
| Recoverable round trip | Quarantine only generated copies, restart, review/restore the batch under the same profile; keep file and original contents survive | Not tested | |
| Narrator | Meaningful control names, values, groups, keep/removal intent, errors and completion are announced in a usable order | Not tested | |
| Contrast theme | Text, inputs, focus, disabled states, selected rows, warnings and review actions remain distinguishable | Not tested | |
| 100% scale | Main window and all dialogs remain usable with no inaccessible essential actions | Not tested | |
| 125% scale | Same check, including long paths and populated results | Not tested | |
| 150% scale | Same check | Not tested | |
| 175% scale | Same check | Not tested | |
| 200% scale | Same check | Not tested | |
| Native Explorer folder drop | Dropping generated folders adds the intended roots without starting an unintended scan | Not tested | |
| Repeated fresh launches | Reopen and scan generated data several times; record any recurrence of the historical first-scan I/O failure | Not tested | |

Narrator can be started/stopped with Windows+Ctrl+Enter when its shortcut is enabled; see [Microsoft's Narrator guide](https://support.microsoft.com/en-us/accessibility/windows/narrator/chapter-1-introducing-narrator). Choose contrast themes through Settings > Accessibility > Contrast themes; see [Microsoft's instructions](https://support.microsoft.com/en-us/accessibility/windows/change-color-contrast-in-windows). Scaling is under Settings > System > Display > Scale; see [Microsoft's display guide](https://support.microsoft.com/en-gb/windows/hardware/display-graphics/change-your-screen-resolution-and-layout-in-windows?nochrome=true).

Record the original settings before testing and restore your preferred settings afterward. Use offered scaling choices; mark unavailable choices Not tested. Application theme automation does not count as contrast-theme validation, and UI Automation labels do not count as a Narrator listening test. Do not run the automated keyboard suite while manually changing settings or interacting with other windows.

## Checks requiring other environments or deliberate storage fault testing

| Check | Result | Evidence required |
| --- | --- | --- |
| Separate supported Windows machine without SDK | Not tested | Candidate/hash, Windows build, ordinary-user launch, scan, previews and recovery |
| Other Windows accounts/profiles | Not tested | Intended account/permission behavior without sharing personal profile data |
| Broader storage/file systems and recovery-write permissions | Not tested | Explicitly identified test medium/file system and synthetic reproduction |
| Disk-full, disconnect or interrupted recovery | Not tested | Controlled disposable test environment and verified recovery outcome |
| Physical SSD/HDD performance and long-session memory | Not tested | Repeatable workload, storage class, metrics and test conditions |

Do not simulate disk failures or power loss on personal data. The existing isolated NTFS listing/sharing checks are narrower than this matrix.

## Issue record

- Affected row and result:
- Candidate commit/hash and Windows/scaling configuration:
- Small reproduction with generated files:
- Expected outcome:
- Actual visible outcome, without identifying paths:
- Reproduced again? Yes / No / Not tested

If the old scan I/O failure recurs, record whether it happened during folder selection, scan startup, scanning or review, and whether the same generated fixture reproduces it. Successful retries do not establish its cause or prove it fixed.

## Scope decision

Current owner-approved scope: **private testing only**. Public licensing, signing and public-release approval remain deferred. Unverified rows stay open; completing this document or merging a PR does not automatically approve a public release.
