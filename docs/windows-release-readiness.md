# Windows release-readiness review

Reviewed 23 November 2026 from merged acceptance baseline `96800e7`.

## Approved distribution scope

The owner selected **private testing only**. Continue providing unsigned, self-contained Windows x64 portable test candidates for local evaluation. Public distribution, an application license, signing/publisher identity, installer/updater work and a public Windows v1 tag are deferred. This review does not grant public-release approval or choose application license terms.

## Completed in this review

- Baseline Release build passed with zero warnings/errors; all 240 tests passed.
- Verified .NET 10.0.15 against official current servicing metadata. No runtime/dependency-version change was necessary. Packaging now refuses stale/unsupported runtime pins or an unavailable/incomplete support review, and preserves the dated review in the ZIP.
- Matched all six embedded Inter fonts to the pinned Avalonia source bytes. Their version metadata identifies Inter 3.019 and source revision `0a5106e0b`; captured that revision's OFL notice. Packaging checks each embedded byte hash/version and the notice hash without loading/executing the font assembly.
- Added a relative-path, SHA-256 notice-file inventory while preserving original package/runtime notices. Broader native-transitive and developer-tool terms review remains open.
- Marked candidate metadata, startup instructions and the portable guide as private testing only. Application services, UI behavior, public contracts, dependency versions and cleanup guarantees are unchanged.
- Changed the manual hosted workflow to verify the package without uploading a downloadable candidate artifact. Private testing candidates remain local; no release tag or public binary distribution is created.
- Ran the revised [hosted packaging workflow](https://github.com/wikijames/DuplicateFinder/actions/runs/45140516551) on `f6b2ffd`: all steps passed, including build/tests, audited packaging and archive verification. The run retained zero artifacts. This hosted machine has an SDK, so it does not close clean-machine/no-SDK acceptance.

## Support and acceptance boundary

| Area | Current position | Required before expanding support/public release |
| --- | --- | --- |
| Windows/architecture | Windows 11 x64 is the private candidate target | Certify exact supported builds on ordinary-user clean environments; no claim for Windows 10, ARM64, x86 or Server |
| Deployment | Portable ZIP, bundled runtime, all files kept together | Clean-machine/native-prerequisite validation; installer/updater work is deferred |
| Storage | Current synthetic Windows/NTFS guard coverage | Intended removable/file-system matrix, permissions, disconnected/full storage and recovery faults |
| Accessibility/display | Existing keyboard/UI Automation, themes and compact layouts | Narrator, high contrast and full 125–200% rendered/display checks |
| Safety | Explicit keep choice, confirmation, guarded quarantine/restore, separately confirmed permanent deletion | Retain guarantees and complete acceptance; historical intermittent scan IOException remains undiagnosed |
| Public distribution | Deferred by owner | License/distribution terms, complete attribution/security review, signing/support policy and final acceptance approval |
| Repository | Hosted build/tests and manual validation-only packaging passed; development previously reported unprotected | Decide protection policy; no repository settings were changed here |

Use [Windows acceptance](windows-acceptance.md) for the exact manual matrix and clean-machine handoff. Only the development laptop is currently available, so the clean-machine row remains Not tested. A later successful retry does not close the historical I/O issue.

## Candidate review commands

```powershell
powershell -NoProfile -File tools/Publish-WindowsPortable.ps1 -DotNetPath 'C:/Program Files/dotnet/dotnet.exe'
powershell -NoProfile -File tools/Test-WindowsAcceptance.ps1 -ZipPath <your-candidate.zip> -Repetitions 3
```

Build/publish uses the SDK and network; the acceptance runner needs neither an SDK invocation nor a repository credential. The publisher now additionally needs Microsoft's official servicing endpoint. Never run untrusted candidates through these tools. Preserve the ZIP checksum, source commit and sanitized acceptance report when recording results.

For each later candidate, re-run the NuGet and runtime checks, verify fonts/notices, build/test, verify the archive and collect acceptance evidence. This is a repeatable private-testing path, not a claim that all release gates are complete.

## Final local candidate evidence

The committed `f6b2ffd` private candidate passed three fresh-process acceptance repetitions. Its ZIP SHA-256 is `7372F79C473D840698E116D5B71D968A43144403F0DBF5C00C67EE2145B0E2B0`; all 262 manifest entries verified. Packaging inputs were committed, metadata states private testing only, and each UI process loaded its bundled runtime. The inventory records 34 notice files and six verified embedded fonts. NuGet reported zero known mapped package vulnerabilities, and the official servicing check confirmed .NET 10.0.15 was current at packaging time.

Negative checks rejected an older runtime patch and an altered embedded-font copy. Font inspection was static and the altered copy was synthetic; no original dependency/file was changed. The previous scan I/O failure did not recur, but its cause remains undiagnosed. Clean-machine, display/accessibility and broader storage acceptance remain open.
