# Windows portable acceptance checks

This phase adds repeatable local evidence for a reviewed Windows x64 portable candidate. Passing it does not certify a public release or replace clean-machine, accessibility, storage and licensing reviews. Application algorithms and cleanup/recovery services are unchanged.

## Repeatable local checks

After building an audited candidate with `tools/Publish-WindowsPortable.ps1`, run from the repository/source archive:

```powershell
powershell -NoProfile -File tools/Test-WindowsAcceptance.ps1 -ZipPath <your-candidate.zip> -Repetitions 3
```

Use only your own trusted candidate. The runner executes it. Neither this runner nor its verification/UI helpers invoke the .NET SDK. Windows PowerShell 5.1 and an interactive Windows desktop are required; keyboard automation needs the test app to retain focus. Do not use the desktop during the check. The current UI harness must match the candidate's supported controls.

The runner:

1. Verifies the ZIP checksum, archive paths, file hashes and package configuration, then extracts to a new local-only directory. It refuses candidates whose packaging inputs were uncommitted.
2. Sets process-local `DOTNET_ROOT`, `DOTNET_ROOT_X64` and `DOTNET_ROOT_X86` to an empty directory, plus the legacy `DOTNET_MULTILEVEL_LOOKUP=0` setting. Child app processes inherit these values. The report records environment overrides, not proof that every installed-runtime discovery mechanism is unavailable. .NET 7 and later already use single-location framework lookup; see [Microsoft's lookup documentation](https://learn.microsoft.com/en-us/dotnet/core/compatibility/deployment/7.0/multilevel-lookup). Existing values are restored in `finally`; no user/machine environment or installed runtime is changed.
3. Runs the existing generated-fixture Windows UI checks in a fresh app process for each repetition. Each process must load `coreclr.dll` from the extracted application directory. Coverage includes the initial scan, text/image previews, protected keep choice, cancelled cleanup, themes, compact layouts, diagnostics, keyboard checks, incomplete-scan notice and 301 groups.
4. Records elapsed time, app CPU time and process-lifetime peak working set for each successful repetition. These are observations of the whole UI smoke workload, not scan throughput, memory-retention bounds or a benchmark comparison.
5. Writes `local-only/acceptance/<run>/acceptance-report.json` with source commit, ZIP hash, harness hashes, Windows build, per-run status, metrics and explicit remaining gates. It stores no absolute paths, machine/user names, file contents, exception messages or stack traces. Failures retain only their exception type, cause a failed result and are not treated as successful retries. All requested repetitions run so intermittency remains visible.

The extraction, generated fixtures and empty-runtime folder are retained. By default the runner performs no confirmed cleanup. A failed report still needs investigation using a synthetic reproduction. Environment isolation and loading the bundled runtime are useful evidence; they do **not** prove absence of installed SDKs or unrelated native/graphics dependencies on the test machine.

The default command remains cancellation-only. For explicit confirmed quarantine/restore using **only a newly generated fixture**, add `-IncludeRecoveryRoundTrip`; see [the recovery UI check and safety boundary](windows-recovery-acceptance.md). The separate recovery result is recorded in the acceptance report and a failure fails the overall run. No permanent deletion is confirmed.

## Manual release acceptance matrix

Record **Pass**, **Fail** or **Not tested**, candidate commit/hash, Windows build and relevant display/storage configuration. Use generated/copied fixtures only and retain recovery batches. Do not collect personal paths or reset global security/display settings through automation.

| Area | Acceptance evidence needed | Current boundary |
| --- | --- | --- |
| Clean machine | Launch, scan, previews, quarantine and restore as an ordinary user on a separate supported Windows machine without the SDK | Not certified by runtime-isolation tests |
| First launch/scan | Repeated fresh launches, selected folders, clear issue summaries and no unexplained I/O failures | Historical intermittent packaged-scan IOException remains undiagnosed even if current runs pass |
| Accessibility | Narrator reads controls, group/keep/removal intent and warnings; complete keyboard-only path and visible focus; high-contrast review | UI Automation and selected keyboard checks are partial evidence |
| Display scaling | 100/125/150/175/200%, small windows, long filenames/paths, both themes and system appearance | Compact sizing is automated; rendered/DPI matrix remains manual |
| Native folder input | Explorer drag/drop, picker, overlapping/missing folders, no accidental scan | Managed scan-folder checks do not certify native drag/drop |
| Storage and permissions | NTFS plus intended removable/file systems; denied access, disconnected drives, full storage and interrupted recovery | Existing synthetic guards do not certify physical faults/power loss |
| Performance | Repeat standard synthetic workloads on explicitly identified SSD/HDD; capture throughput, allocation, peak memory and cancellation latency | Local uncontrolled observations are not SSD/HDD or cold-cache claims |
| Portable replacement/removal | Close, extract newer candidate separately, launch it; retain batches/logs independently | No installer, updater or application-data purge is provided |
| CI and distribution | Hosted build/tests/package workflow, branch protection, support policy, licensing/attribution, runtime review and signing decision | Local tests do not close these release approvals |

Do not mark the whole Windows acceptance phase complete until the required manual rows have evidence. Similarity matching, Android, an installer and additional preview formats are outside this release-validation phase.

## Separate clean-machine handoff

1. Use a separate ordinary-user Windows 11 x64 environment without the .NET SDK. Record its Windows build, scaling and whether any .NET runtimes/native prerequisites were already installed. Do not uninstall development tools from the development laptop to simulate this.
2. Copy the complete candidate ZIP and checksum, verify the SHA-256, and extract everything to a new folder you own. Launch `DuplicateFinder.Desktop.exe` directly. Record any Windows security/graphics/prerequisite prompt; do not disable security controls to bypass it.
3. Create a dedicated test-data folder with three byte-identical generated text files and three copies of one synthetic PNG/JPEG. Scan it, inspect both groups, change the keep choice and verify text/image previews. Repeat after closing and freshly launching the app three times.
4. First cancel the quarantine review and check that all fixtures remain. Then, using only these disposable copies, confirm quarantine of one group to a separate same-drive test batch; verify the kept file remains. Restore that batch using the same Windows user profile and verify file counts and SHA-256 values. Do not test permanent deletion or power loss on real data.
5. Check keyboard-only navigation, Narrator, scaling and themes against the matrix. Close the app, extract a replacement candidate separately and verify it launches. Retain test batches until recovery has been verified; application-folder removal must not remove recovery data.
6. Record each row as Pass/Fail/Not tested with the candidate commit/hash. For any failure, describe a synthetic reproduction and the visible status without personal paths. A failed or untested required row keeps Windows v1 release approval open.

## Local observations — 23 November 2026

The merged application baseline `6a7025f` built Release with zero warnings/errors and passed all 240 tests. A fresh NuGet query covered all four projects and reported zero known mapped package vulnerabilities; this does not replace native-code/runtime security review.

The audited portable baseline ZIP had SHA-256 `BBB9574EB22A9FAD2A469351F00F689DD6B12C1F156B9138039B7FFEB2D2C754`; all 259 manifest entries passed verification. Three acceptance repetitions passed, each confirming the bundled runtime. Whole-UI-workload elapsed times were 25.58, 24.80 and 24.91 seconds; process-lifetime peak working sets were 257,417,216, 267,030,528 and 267,558,912 bytes. Original runtime environment values were confirmed restored, and the resulting JSON had no absolute local paths. These fresh-process observations do not measure long-session memory retention or certify the historical I/O issue as fixed.

The existing standard synthetic workloads also validated: small duplicates 3021.418 ms, mostly unique 1373.676 ms, large duplicates 281.982 ms, and observed cancellation latency 1.655 ms. Scan allocations were respectively 271,068,776, 3,948,048, 347,752 and 99,008 bytes. Storage/cache/background load were not controlled; these are single local observations, not a speedup or SSD/HDD comparison. No concurrency or application algorithm was changed in response to these measurements.

The committed acceptance candidate `c48dd4b` also passed all three packaged repetitions. [Hosted build and test for merged development `6a7025f`](https://github.com/wikijames/DuplicateFinder/actions/runs/45136094422) completed successfully. The repository branch API reported `development` as unprotected at review time; changing protection rules requires a separate repository-owner decision. The manual portable workflow had zero recorded runs at review time. Its hosted verification and the final branch-protection policy remain release gates.
