# Windows release preparation

This phase prepares an unsigned portable test candidate from merged development, without changing application scan, verification, cleanup, restore or diagnostics behavior. It does not tag or approve a public release.

## Build and inspect a candidate

Use the .NET 10 SDK and an interactive Windows session for UI tests:

```powershell
dotnet restore DuplicateFinder.slnx -p:Configuration=Release --locked-mode
dotnet build DuplicateFinder.slnx -c Release --no-restore
dotnet test DuplicateFinder.slnx -c Release --no-build --no-restore
powershell -NoProfile -File tools/Publish-WindowsPortable.ps1 -DotNetPath "C:\Program Files\dotnet\dotnet.exe"
```

The packager prints a new ZIP path under ignored `local-only/packages/`. It never removes or reuses an existing output directory. It publishes Windows x64 with the profile-pinned runtime, no trimming/single-file bundling, no managed/native PDBs and no developer diagnostics bridge. The application-source/build-input dirty flag is recorded; the unrelated README/research edits are not packaged. This is a repeatable recipe and dependency selection, not a claim of byte-identical ZIPs: timestamps, SDK versions and build inputs can differ.

The ZIP includes `START-HERE.txt`, the [portable user guide](windows-portable-guide.md), runtime binaries, available dependency licenses/notices, `dependency-inventory.json`, a dated sanitized NuGet vulnerability report, `package-info.json` and a relative-path SHA-256 file manifest. The manifest excludes its own file; the ZIP hash sidecar covers the whole archive. Supplemental notices and their source hashes are in [license provenance](licenses/PROVENANCE.md).

Verify the package before running it. Only use the UI switch for an executable built from reviewed, trusted source:

```powershell
powershell -NoProfile -File tools/Test-WindowsPortable.ps1 -ZipPath "<generated ZIP>"
powershell -NoProfile -File tools/Test-WindowsPortable.ps1 -ZipPath "<generated ZIP>" -RunUiSmokeTest
```

The verifier checks the sidecar, archive paths, all manifested file lengths/hashes, unexpected files, required configuration and absence of absolute local paths in generated reports. It retains a fresh extraction for inspection. The optional UI check starts only the selected candidate in its own process, verifies that it loaded `coreclr.dll` from its own directory, and runs generated-fixture review checks. Checksums do not authenticate an unknown publisher; do not run third-party/untrusted executables through this tool.

## CI

Normal CI builds/tests Release using its matching locked desktop graph. The separate **Windows portable test candidate** workflow is manual (`workflow_dispatch`) and retains only the generated ZIP and checksum for seven days. It does not upload private notes, the source workspace or other local-only content. The current repository default branch is development; the new workflow becomes available there after merge. Select the intended source branch when triggering it. Hosted execution has not been verified in this phase.

The manual workflow runs the same build/tests, audit and package checks before retaining an artifact. It does not perform GUI tests on hosted agents, create a release tag or publish a final release. Local packaging works independently of Actions.

## Local validation

- Locked Debug and Release desktop restores succeeded. Both build configurations succeeded without warnings/errors; all 215 existing Release tests passed.
- The source vulnerability query covered all four projects and reported no known NuGet vulnerabilities. The Release license-metadata snapshot covers 40 unique package/version entries; see [dependency findings and open items](dependency-review.md).
- A successful candidate contained 26 restored desktop packages, 19 published graph entries and no developer bridge. x64 PE checks passed for the apphost, runtime and native renderers. Included-runtime configuration matched .NET 10.0.15.
- ZIP checksum and 259 file manifest entries passed after extraction. Synthetic bad-checksum and path-traversal ZIPs were rejected before extraction/execution. No real files were deleted.
- Three fresh-process UI runs of the extracted candidate passed. These covered first scans, 301 generated groups, keep-choice preservation, preview-only selection, cancelled cleanup confirmations, compact dialogs, themes, keyboard navigation and a persistent hard-link notice. The candidate loaded its bundled runtime. The earlier initial packaged-scan IOException did not reproduce here; its cause remains undiagnosed.

The build machine has SDKs/native dependencies installed. A bundled-runtime check is useful evidence but is not a clean-machine/no-SDK test. No SDK was removed and no global Windows security/display settings were changed.

## Acceptance before public release

- Choose the application license/distribution terms; close native/font attribution and developer-tool terms review.
- Review the pinned runtime against current supported/security releases, then intentionally update the profile/locks/notices if needed.
- Define and validate the minimum supported Windows build; Windows 11 x64 is an initial test target, not the complete support matrix. Installer support is deferred.
- Test a clean Windows machine without the SDK, including graphics/native prerequisites, ordinary user permissions and launch behavior.
- Decide publisher identity and code-signing/distribution policy. Current candidates are unsigned, with no installer, updater or installation registration.
- Complete Narrator, high contrast, 125–200% scaling, storage/filesystem, interrupted recovery and physical SSD/HDD checks.
- Verify hosted CI, branch protection and the manual candidate workflow; investigate any recurrence of the initial scan I/O failure.

Use the [Windows release checklist](windows-release-checklist.md) for the remaining gates. A successful feature PR is not a Windows v1 release certification.
