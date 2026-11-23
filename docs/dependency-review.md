# Dependency review snapshot

Reviewed 15 November 2026 from Release restore metadata. This is an inventory and release-preparation record, not a legal opinion or a security certification. NuGet vulnerability queries included direct/transitive dependencies for all four solution projects and reported zero known vulnerabilities. The generated package report records the query time and source; repeat it for every candidate.

The Release solution has 40 unique package/version entries. The desktop restore graph has 26; 19 appear in the published dependency graph. NuGet metadata declares 31 MIT expressions, seven Apache-2.0 expressions, one license file (ANGLE) and one legacy license URL (test-only xunit.abstractions). These declarations do not describe every embedded native/font component.

| Package/version | Declared license | Restore scope |
| --- | --- | --- |
| Avalonia.Angle.Windows.Natives/2.1.27548.20260419 | file: LICENSE | Desktop, Tests |
| Avalonia.BuildServices/11.3.2 | MIT | Desktop, Tests |
| Avalonia.Desktop/12.1.6 | MIT | Desktop, Tests |
| Avalonia.Fonts.Inter/12.1.6 | MIT | Desktop, Tests |
| Avalonia.FreeDesktop.AtSpi/12.1.6 | MIT | Desktop, Tests |
| Avalonia.FreeDesktop/12.1.6 | MIT | Desktop, Tests |
| Avalonia.HarfBuzz/12.1.6 | MIT | Desktop, Tests |
| Avalonia.Native/12.1.6 | MIT | Desktop, Tests |
| Avalonia.Remote.Protocol/12.1.6 | MIT | Desktop, Tests |
| Avalonia.Skia/12.1.6 | MIT | Desktop, Tests |
| Avalonia.Themes.Fluent/12.1.6 | MIT | Desktop, Tests |
| Avalonia.Win32/12.1.6 | MIT | Desktop, Tests |
| Avalonia.X11/12.1.6 | MIT | Desktop, Tests |
| Avalonia/12.1.6 | MIT | Desktop, Tests |
| coverlet.collector/6.0.4 | MIT | Tests |
| HarfBuzzSharp.NativeAssets.Linux/8.3.1.3 | MIT | Desktop, Tests |
| HarfBuzzSharp.NativeAssets.macOS/8.3.1.3 | MIT | Desktop, Tests |
| HarfBuzzSharp.NativeAssets.WebAssembly/8.3.1.3 | MIT | Desktop, Tests |
| HarfBuzzSharp.NativeAssets.Win32/8.3.1.3 | MIT | Desktop, Tests |
| HarfBuzzSharp/8.3.1.3 | MIT | Desktop, Tests |
| MicroCom.Runtime/0.11.6 | MIT | Desktop, Tests |
| Microsoft.CodeCoverage/17.14.1 | MIT | Tests |
| Microsoft.NET.Test.Sdk/17.14.1 | MIT | Tests |
| Microsoft.TestPlatform.ObjectModel/17.14.1 | MIT | Tests |
| Microsoft.TestPlatform.TestHost/17.14.1 | MIT | Tests |
| Newtonsoft.Json/13.0.3 | MIT | Tests |
| SkiaSharp.NativeAssets.Linux/3.119.4 | MIT | Desktop, Tests |
| SkiaSharp.NativeAssets.macOS/3.119.4 | MIT | Desktop, Tests |
| SkiaSharp.NativeAssets.WebAssembly/3.119.4 | MIT | Desktop, Tests |
| SkiaSharp.NativeAssets.Win32/3.119.4 | MIT | Desktop, Tests |
| SkiaSharp/3.119.4 | MIT | Desktop, Tests |
| Tmds.DBus.Protocol/0.94.1 | MIT | Desktop, Tests |
| xunit.abstractions/2.0.3 | legacy license URL; review | Tests |
| xunit.analyzers/1.18.0 | Apache-2.0 | Tests |
| xunit.assert/2.9.3 | Apache-2.0 | Tests |
| xunit.core/2.9.3 | Apache-2.0 | Tests |
| xunit.extensibility.core/2.9.3 | Apache-2.0 | Tests |
| xunit.extensibility.execution/2.9.3 | Apache-2.0 | Tests |
| xunit.runner.visualstudio/3.1.4 | Apache-2.0 | Tests |
| xunit/2.9.3 | Apache-2.0 | Tests |

## Distribution and review findings

- The Windows candidate is self-contained with .NET 10.0.15 pinned in `WindowsPortable.pubxml`. The runtime's own license and third-party notices are copied from its restored runtime pack. NuGet package-advisory results do not certify the runtime patch or every embedded native component; review runtime security releases before each public build.
- All 19 packages in the published dependency graph have a modern declared license field. The 26-package restore inventory distinguishes entries in that graph from build/other-platform entries; it does not claim that each graph entry has its own physical DLL in the ZIP.
- `AvaloniaUI.DiagnosticsSupport` 2.2.3 has no license field in its cached NuGet metadata. It is now a Debug-only reference with private assets. Its bridge and developer-only dependencies are absent from the Release graph and candidate. Developer-tool terms still require separate review; exclusion is not a license determination.
- Test-only `xunit.abstractions` 2.0.3 uses a legacy license URL instead of a modern license field. The referenced [upstream license](https://raw.githubusercontent.com/xunit/xunit/master/license.txt) currently describes Apache-2.0 with component-specific MIT material. This mutable URL is not proof of the historical binary's full terms. Test assemblies/packages are not redistributed in the app candidate.
- Avalonia wrapper packages declare MIT; their notice and original copyright metadata accompany the candidate. Native Skia/HarfBuzz licenses and third-party notices are preserved from their packages. ANGLE's package-provided LICENSE is retained. Native embedded/transitive attribution still needs closure.
- Inter's font license is separate from its MIT-licensed wrapper. The upstream OFL text is included with source/hash provenance; exact embedded-font provenance/version matching remains open.
- No application LICENSE is present. The owner must choose distribution/license terms before a public release. This work does not choose a license for DuplicateFinder or certify legal compliance.

The successful draft candidate measured about 106 MiB expanded and 46 MiB zipped after excluding native/managed PDBs. Exact file sizes, hashes, runtime/SDK versions, source commit and modified-input flag are generated for each package. Hash sidecars detect changes; they are not publisher authentication or code signing.

## Evidence and repeatability

`Publish-WindowsPortable.ps1` refreshes a direct/transitive vulnerability query against the public NuGet source and refuses to create a ZIP if reported vulnerabilities are present. Query failure/incomplete reports also stop packaging. Generated project paths are made relative before being placed in the candidate. The release inventory records package hashes from restore metadata and copies available notices without modifying their text.

Desktop Debug and Release dependency graphs are locked separately; build/test tools are reviewed here but are not all lock-file pinned. CI restores Release explicitly before its Release build so Debug-only packages cannot leak through a mismatched restore. A lock constrains dependency resolution, not known-vulnerability status. Re-run audits and intentionally review lock changes when updating dependencies.

Primary references: [NuGet audit behavior](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages), [NuGet lock files and conditional references](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies), [.NET self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [notice provenance](licenses/PROVENANCE.md).
