# Supplemental dependency notice provenance

These are upstream dependency notices, not the application license. They are copied into portable test candidates alongside available package-provided licenses/notices and original NuGet metadata.

| File | Source | SHA-256 |
| --- | --- | --- |
| Avalonia-MIT.txt | https://raw.githubusercontent.com/AvaloniaUI/Avalonia/cf8e564674700348947e93ab19105f27afe859a9/licence.md | 213814D306090074D234D760239FF0F67EB9B8D20EEFB4D5631BB39DBE0B769B |
| Inter-OFL.txt | https://raw.githubusercontent.com/rsms/inter/master/LICENSE.txt | 262481E844521B326F5ECD053E59B98C8B2DA78C8EE1BDBB6E8174305E54935A |
| MIT-template.txt | https://raw.githubusercontent.com/spdx/license-list-data/main/text/MIT.txt | B05785F9F18E6716BAB63424B11454513B9943A222595B70411009202FC592B5 |

Collected 15 November 2026. The mutable upstream URLs identify where the captured bytes came from; the hashes identify the committed copies. The MIT template is standard expression text, not a substitute for each component's original copyright. Package copyright declarations remain in the inventory and unmodified `.nuspec` files. Avalonia's notice is pinned to the repository commit declared by the installed 12.1.6 packages.

Git attributes preserve the captured license text bytes without line-ending conversion, including on Windows checkouts.

## Embedded Inter fonts — reviewed 23 November 2026

All six TTF byte sequences inside the installed `Avalonia.Fonts.Inter` 12.1.6 assembly match their upstream files at Avalonia commit `cf8e564674700348947e93ab19105f27afe859a9`. Their OpenType name tables identify `Version 3.019;git-0a5106e0b`, and identify the Inter Project Authors and OFL-1.1. No third-party assembly or font was executed to inspect this data.

`Inter-font-provenance.json` records each font's byte length, full SHA-256, Git blob, pinned source URL and the matching notice hash. `Test-EmbeddedFontNotices.ps1` statically checks those byte sequences and version records before packaging. Changed or missing fonts/notices stop packaging for review.

The matching notice is `Inter-embedded-OFL.txt`, captured verbatim from https://raw.githubusercontent.com/rsms/inter/0a5106e0b/LICENSE.txt with SHA-256 `4D7D9C95E7D7F2F0EBF76D5E0B344826B74E903A34028EE18AB54BB639E45906`. It includes the 2016–2020 Inter Project Authors notice. The previously captured `Inter-OFL.txt` remains a separately identified supplemental upstream notice; it is not used as a substitute for this revision-matched notice.

Native transitive attribution still needs review before public distribution. Including these files does not establish complete compliance, change upstream terms or select a license for DuplicateFinder.
