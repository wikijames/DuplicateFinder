# Private-testing handoff

This is the consolidated handoff for the Windows exact-duplicate application. The approved scope is **private testing only**. Public distribution, application license/signing decisions and a Windows v1 release tag are deferred.

## Implemented and locally exercised

- Exact duplicate scanning with size/hash/byte verification, supported filters and multiple roots.
- Split-view review, protected keep selection, search/sort, text previews and bounded PNG/JPEG comparison.
- Explicit guarded quarantine and restore, cancellation, restore after app restart, conflict refusal and safe repeat restore.
- System/Light/Dark appearance, local keyboard/focus checks and the recovery-selector compact-window fix.
- Opt-in bounded diagnostics and persistent incomplete-scan warnings.
- Audited self-contained x64 packaging, current .NET servicing checks, verified embedded Inter fonts and hashed notices.
- Synthetic UI/recovery and real isolated NTFS listing/sharing tests, including restoration after an intentional harness failure.

The detailed evidence and scope are in [Windows acceptance](windows-acceptance.md), [recovery acceptance](windows-recovery-acceptance.md), [permission acceptance](windows-permission-acceptance.md) and [release readiness](windows-release-readiness.md). These local checks do not certify all Windows/storage/accessibility configurations.

## What remains

| Category | Current status | Next action |
| --- | --- | --- |
| Hands-on accessibility/display | Manual rows unverified; latest fresh keyboard run blocked by foreground permission | Activate the test app for keyboard validation and fill the manual record for Narrator, contrast themes, offered scaling values and native Explorer drop |
| Separate clean machine | Not tested; only development laptop available | Retain as an open gate until an appropriate environment is available |
| Broader storage/accounts/recovery-write failures | Partially covered by isolated NTFS/read-sharing and synthetic service tests | Collect controlled evidence on the intended configurations |
| Long-session memory and physical SSD/HDD measurements | Initial synthetic metrics exist; broader measurements open | Use repeatable workloads and record conditions rather than assuming a speedup |
| Historical intermittent packaged-scan I/O failure | Undiagnosed; later local runs passed | Collect a generated-data reproduction if it recurs; do not label it fixed |
| Full native-transitive attribution/security and developer-tool terms | Open before public distribution | Continue the documented dependency review; font provenance and current servicing checks are narrower completed items |
| Branch protection | Owner policy decision remains open | Review repository settings separately; no protection policy is silently applied |
| Public license/signing/support certification | Deferred by owner | Revisit only when public distribution becomes the objective |

These rows are acceptance or owner decisions, not a new list of planned product features. Consolidate reproducible fixes found during this final review into this handoff PR where practical. Later discoveries may still require fixes; no merge alone completes an untested row.

## Use the candidate

Extract the entire private ZIP and launch `DuplicateFinder.Desktop.exe`. Read `USER-GUIDE.md` and the bundled `MANUAL-ACCEPTANCE.md`. Copy the latter outside the application folder before entering results, keeping the sealed package files unchanged. Start with generated/copied files, retain quarantine manifests and use the original Windows profile for restore.

For the complete scripted local check, from reviewed source on an interactive Windows desktop:

```powershell
powershell -NoProfile -File tools/Test-WindowsAcceptance.ps1 -ZipPath <your-reviewed-candidate.zip> -Repetitions 3 -IncludeRecoveryRoundTrip -IncludePermissionChecks
```

The recovery and permission modules are explicit: they operate only on newly generated fixtures, preserve test data and restore temporary permissions. Ordinary acceptance remains cancellation-only. Neither an SDK-installed machine nor an environment override is a clean-machine/no-SDK certification.

## Remaining manual results

Use [the manual result template](manual-acceptance-results-template.md). Every row starts Not tested; only an observed result for a recorded candidate/configuration can change it to Pass or Fail. No Narrator, contrast-theme or global display setting is changed automatically by this handoff.

## Consolidation baseline — 23 November 2026

Merged baseline `889e330` built Release with zero warnings/errors and passed all 240 existing tests. No application or existing test source is changed by this handoff. Packaging now includes the exact manual-result template and tracks it as a packaging input. The draft archive verified all 263 manifest entries and all 19 manual rows remained Not tested; draft modified inputs were correctly identified. Final candidate identity and acceptance results are recorded with the committed candidate's reports.

## Current final-candidate verification

Candidate `5bcefdd` was built from committed packaging inputs with private-testing metadata. Its ZIP SHA-256 is `CE39203524956DDD4904470E4585A92F2038794549C1550B41284173C661916C`. Archive/template checks passed, including the exact bundled manual record and its 19 unverified rows. Both the seven-stage synthetic recovery check and five-scenario NTFS permission check passed on this candidate, with fixture permissions restored.

The first three fresh general-UI repetitions failed and their reports were retained. A diagnostic rerun pinpointed the recovery-selector Tab assertion; the subsequent foreground guard established that Windows would not grant the test app foreground input. Fresh physical-keyboard verification therefore remains **blocked**, and the complete acceptance result is not marked passed. The application source is unchanged; no root cause is attributed to an application defect from this environment result.

This review also fixed the test harness leaving app processes behind after failed checks, added safe foreground ownership checks and added path-free numeric failure frames. The remaining keyboard rerun needs the operator to activate the test window. No Narrator/contrast/scaling result has been supplied, and no manual row is automatically approved. These findings stay in this consolidation PR rather than being presented as completed release certification.
