# Isolated NTFS permission and sharing acceptance

This check exercises the packaged scan UI with real NTFS listing denial and an exclusive file handle. All sources are newly generated fixtures. No matching, cleanup, quarantine, restore or application contracts are changed.

## Run explicitly

Use Windows PowerShell 5.1, a trusted committed private-testing ZIP and an interactive desktop:

```powershell
powershell -NoProfile -File tools/Test-WindowsPermissionUi.ps1 -ZipPath <your-reviewed-candidate.zip>
```

Or include permission checks in the overall acceptance run:

```powershell
powershell -NoProfile -File tools/Test-WindowsAcceptance.ps1 -ZipPath <your-reviewed-candidate.zip> -Repetitions 1 -IncludePermissionChecks
```

`-IncludeRecoveryRoundTrip` can also be supplied explicitly. Without the permission switch, the overall runner does not invoke permission testing. Default acceptance remains cancellation-only and changes no fixture permissions.

## Fixture boundary and restoration

The tool verifies/extracts the candidate and creates a fresh GUID directory under `local-only/permission-acceptance`. It accepts no scan root, account or ACL-target argument. The source has four matching healthy text files (one used for the sharing test) and two distinct matching text files in a restricted subfolder. Source hashes are captured before testing.

Only the newly created `source/restricted` folder receives a temporary deny-listing rule for the current Windows identity. The rule has no inheritance or propagation. Parent/child ACLs, ownership, other accounts and machine settings are not modified. The tool validates the exact fixture target and rejects reparse-point ancestors before permission changes. Account identifiers and raw access descriptors remain in memory only.

The original access descriptor is reconstructed and restored in `finally`, then compared with its initial access-section SDDL. The exclusive file handle is disposed, and all six source hashes are checked. Fixtures are retained; no cleanup, quarantine, restore or permanent-delete action is invoked by this tool. Normal exceptions also follow this restoration path; abrupt process/power termination remains outside the tested guarantee.

## Expected UI results

| Scenario | Duplicate groups | Files in groups | Skipped files | Skipped folders | Warning |
| --- | ---: | ---: | ---: | ---: | --- |
| Accessible baseline | 2 | 6 | 0 | 0 | Hidden |
| Restricted folder cannot be listed | 1 | 4 | 0 | 1 | Visible |
| Restricted folder plus locked file | 1 | 3 | 1 | 1 | Visible |
| Folder access restored, file still locked | 2 | 5 | 1 | 0 | Visible |
| All access restored | 2 | 6 | 0 | 0 | Hidden |

The tool proves the directory denial and sharing restriction are effective before checking the UI. It requires exact group/file totals, skipped counts, a visible incomplete-scan notice and healthy-group review availability. Filtering to no matches must retain the warning and disable hidden-group cleanup eligibility. A subsequent healthy rescan must clear the stale warning. The inline file total is read through its parent text's accessible name.

## Deliberate failure-path check

For maintainers, `-ExerciseFailureCleanup` intentionally throws after both restrictions are active. It is **expected to exit nonzero** and produce `Passed: false`. Its report must show the fixed injected-failure code, effective restrictions, original folder access restored, exclusive handle released, unchanged fixture contents and no cleanup error. This is a restoration test, not a passing application-acceptance run.

## Evidence and limits

`permission-ui-report.json` contains candidate/ZIP/harness hashes, fixed scenario names, numeric results and restoration booleans. Failures contain only a fixed stage/check code and exception type. No paths, SIDs, raw ACLs, user/machine names, file contents or exception messages are recorded. Overall acceptance links the requested result by report hash; a failed permission result fails the overall run.

This closes only isolated, same-user NTFS read/listing and sharing coverage. Different accounts/profiles, denied writes during recovery, EFS, physical disk-full/disconnect/power-loss behavior, removable/exFAT/ReFS media, clean-machine, accessibility and wider display checks remain open. Private testing remains the approved distribution policy.

## Local results — 23 November 2026

Merged baseline `5e94367` built Release with zero warnings/errors and passed all 240 existing tests. Application and existing test sources are unchanged in this phase. The reviewed private candidate `01fdf80` has the same application source tree and was reused; its ZIP SHA-256 was `B9D7C21567747884299D7423C4184C7BE104FCD8B298EEF5C2D0BE540930C3A1`, with 262 verified manifest entries.

All five permission scenarios passed with the exact totals above. The actual denial/lock probes passed, warnings persisted during no-match filtering, and the recovered complete scan cleared them. The original folder access descriptor, exclusive-handle release and all six fixture hashes were verified after completion.

The deliberate failure run exited nonzero as intended while both restrictions were active. Its failed report identified the injected check, confirmed exact permission restoration and lock release, preserved all fixture contents and recorded no cleanup error. It was not counted as a successful application-acceptance run.

A combined run with one ordinary UI repetition, explicit recovery and explicit permissions passed. The default acceptance run also passed, requested neither optional module, used only the original three harness files and created no permission fixture. This evidence does not certify another account, a clean machine or the wider physical-storage matrix.
