# Synthetic quarantine and restore UI acceptance

This check exercises the existing recovery workflow through a new app process, using a verified, committed private-testing portable candidate. It changes no scanning, hashing, quarantine, restoration or public application contracts.

## Run explicitly

```powershell
powershell -NoProfile -File tools/Test-WindowsRecoveryUi.ps1 -ZipPath <your-reviewed-candidate.zip>
```

Or include it once alongside the existing repeated review checks:

```powershell
powershell -NoProfile -File tools/Test-WindowsAcceptance.ps1 -ZipPath <your-reviewed-candidate.zip> -Repetitions 3 -IncludeRecoveryRoundTrip
```

The ordinary acceptance command without this switch stays cancellation-only. The existing `Test-WindowsReviewUi.ps1` behavior is unchanged. The explicit recovery check **confirms quarantine and restore**, solely for its own newly generated files. It never confirms permanent deletion, accepts no arbitrary source/destination path, and never purges fixtures or recovery data. Execute only trusted DuplicateFinder candidates, on an interactive Windows desktop, and leave the app in focus during UI Automation.

## Scope and safety checks

The runner verifies/extracts the ZIP and creates a fresh GUID directory under `local-only/recovery-acceptance`. Its source contains three byte-identical generated text files plus one unrelated generated sentinel. Its same-drive destination is a new sibling folder. Every operation path must remain inside this fixture.

Before confirming quarantine, the runner requires the UI's protected file to match the nondefault generated keep choice, the action to be **Move to quarantine**, and the complete reviewed target text to contain exactly the two expected distinct fixture paths. The full list is checked via UI Automation text/name content, with scrolling when needed; a label alone or a subset of paths is insufficient. UI searches are scoped to the app process created by the runner.

It verifies these seven stages:

1. Scan the generated folder and select a nondefault keep file.
2. Cancel quarantine and verify source hashes and an empty destination.
3. Confirm quarantine; verify two moved payloads, unchanged keep/sentinel files and a retained protected manifest.
4. Restart the app, reopen the batch without scanning, cancel restore and verify no recovery occurred.
5. Review restore, then create a generated conflicting file before confirmation. Verify refusal, unchanged conflict bytes and untouched payloads. Preserve this test-created conflict by moving it to another new path inside the fixture; no file is deleted.
6. Review again and confirm restore. Verify original file hashes, unchanged keep/sentinel files, no remaining payload files and an unchanged manifest.
7. Review/confirm the already-restored batch again. Verify zero moved files, unchanged sources and the retained immutable manifest.

App shutdown is limited to the runner's own process. If it cannot close normally, that process can be terminated; all fixture/batch contents remain for investigation. Failures stop the scenario, produce a failed result and are not automatically retried or cleaned up.

## Evidence and limits

`recovery-ui-report.json` records the candidate commit/ZIP hash, harness hash, completed stages, elapsed time, bundled-runtime check and sanitized failure information. Failure records contain a fixed stage/check code, exception type and at most a whitelisted control label; they do not contain arbitrary exception messages, source paths, contents, machine/user names or stack traces. The overall acceptance report links a requested recovery result by hash, without embedding its local path. A recovery failure fails the overall requested acceptance run.

This is same-user, synthetic local-file coverage. It does not certify a separate clean machine, another Windows account/profile, Narrator/high contrast, all DPI settings, removable/exFAT/ReFS media, denied permissions, disk-full/disconnect/power-loss scenarios or the historical intermittent scan I/O issue. Those gates remain in [Windows acceptance](windows-acceptance.md). Private testing remains the approved distribution scope.

## Local validation — 23 November 2026

Merged development baseline `bc82b9c` built Release with zero warnings/errors and passed all 240 existing tests. No application or existing test source changed in this phase. Its application source tree matches the reviewed private candidate `f6b2ffd`; the existing candidate was reused rather than implying a new application feature/build was required.

The candidate ZIP SHA-256 was `7372F79C473D840698E116D5B71D968A43144403F0DBF5C00C67EE2145B0E2B0`, and all 262 manifest entries verified. The standalone recovery scenario passed all seven stages. A default one-repetition acceptance run passed and created no recovery fixture; its report recorded recovery as not requested. The combined three-repetition run with `-IncludeRecoveryRoundTrip` then passed, including all seven recovery stages and the additional payload/manifest checks. Both recovery app launches loaded the bundled runtime.

The combined report recorded recovery report SHA-256 `C3D921DA13E93817A2BA096939C4F33615BC9681EDB74092F6BF4733ECF61981`. Reports remain local-only; no paths or generated contents are embedded in this evidence record. The restored files, unrelated sentinel, preserved conflict and immutable manifest remain in the generated fixture for inspection. No permanent deletion was confirmed and no real user data was scanned or moved.
