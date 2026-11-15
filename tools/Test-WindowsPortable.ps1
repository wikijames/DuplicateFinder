param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [switch]$RunUiSmokeTest
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$zip = (Resolve-Path -LiteralPath $ZipPath).Path
$checksumFile = $zip + '.sha256'
if (!(Test-Path -LiteralPath $checksumFile)) { throw 'The package checksum sidecar is missing.' }
$expected = ((Get-Content -LiteralPath $checksumFile -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'Package checksum mismatch.' }
$extract = Join-Path $repoRoot ('local-only/packages/verified-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $archive.Entries) {
        $target = [IO.Path]::GetFullPath((Join-Path $extract $entry.FullName))
        if (!$target.StartsWith($extract + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A ZIP entry would escape the extraction directory.'
        }
    }
}
finally { $archive.Dispose() }
Expand-Archive -LiteralPath $zip -DestinationPath $extract
$roots = @(Get-ChildItem -LiteralPath $extract -Directory)
if ($roots.Count -ne 1) { throw 'Expected one portable application directory.' }
$root = $roots[0].FullName
$manifest = Get-Content -LiteralPath (Join-Path $root 'file-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $entry.File))
    if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes the package.' }
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $entry.Bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) { throw 'A package file does not match its manifest.' }
}
$allFiles = @(Get-ChildItem -LiteralPath $root -File -Recurse)
if ($allFiles.Count -ne $manifest.Count + 1) { throw 'Unmanifested package files were found.' }
if (@($allFiles | Where-Object { $_.Extension -in @('.pdb', '.jsonl', '.log') -or $_.Name -match '(?i)^AvaloniaUI\.' -or $_.Name -eq 'PROJECT_CONTEXT.md' }).Count -gt 0) {
    throw 'The archive contains development or local files.'
}
foreach ($name in @('package-info.json', 'dependency-inventory.json', 'nuget-vulnerability-report.json', 'file-manifest.json')) {
    if ((Get-Content -LiteralPath (Join-Path $root $name) -Raw) -match '"[A-Za-z]:[\\/]') { throw 'A generated report contains a local absolute path.' }
}
$info = Get-Content -LiteralPath (Join-Path $root 'package-info.json') -Raw | ConvertFrom-Json
if (!$info.SelfContained -or $info.RuntimeIdentifier -ne 'win-x64') { throw 'Candidate configuration is incorrect.' }
if ($RunUiSmokeTest) {
    # This executes the user-selected candidate. Use only your own trusted build.
    & (Join-Path $PSScriptRoot 'Test-WindowsReviewUi.ps1') -ExecutablePath (Join-Path $root 'DuplicateFinder.Desktop.exe')
}
[pscustomobject]@{ Result = 'PASS'; VerifiedFiles = $manifest.Count; SourceCommit = $info.SourceCommit; ExtractedDirectory = $root; UiSmokeTest = [bool]$RunUiSmokeTest }
# Retain the generated extraction and synthetic fixtures for inspection; never remove user data.
