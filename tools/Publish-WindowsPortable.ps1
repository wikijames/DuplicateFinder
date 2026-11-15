param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This packaging script requires Windows.' }
$dotnet = (Get-Command $DotNetPath -CommandType Application -ErrorAction Stop).Source
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = 'src/DuplicateFinder.Desktop/DuplicateFinder.Desktop.csproj'
[xml]$profile = Get-Content -LiteralPath (Join-Path $repoRoot 'src/DuplicateFinder.Desktop/Properties/PublishProfiles/WindowsPortable.pubxml')
$runtimeVersion = [string]$profile.Project.PropertyGroup.RuntimeFrameworkVersion
if ($runtimeVersion -notmatch '^10\.0\.\d+$') { throw 'The portable profile must pin a .NET 10 runtime patch.' }

function Invoke-DotNet([string[]]$Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'A dotnet packaging step failed.' }
}
function Assert-X64Image([string]$Path) {
    $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($Path))
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'A native package file is not a PE image.' }
        $reader.BaseStream.Position = 0x3C
        $offset = $reader.ReadInt32()
        $reader.BaseStream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'A native package file is not Windows x64.' }
    }
    finally { $reader.Dispose() }
}

Push-Location $repoRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'A Git source commit is required.' }
    $dirty = @(& git status --porcelain -- .gitattributes src tools/Publish-WindowsPortable.ps1 tools/Write-DependencyInventory.ps1 docs/licenses docs/windows-portable-guide.md).Count -gt 0
    $name = 'DuplicateFinder-' + $commit.Substring(0, 7) + '-win-x64'
    $runRoot = Join-Path $repoRoot ('local-only/packages/' + $commit.Substring(0, 7) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    if (Test-Path -LiteralPath $runRoot) { throw 'The package output already exists. Existing outputs are never removed or overwritten.' }
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    $publish = Join-Path $runRoot $name

    Invoke-DotNet @('restore', $project, '-r', 'win-x64', '-p:Configuration=Release', "-p:RuntimeFrameworkVersion=$runtimeVersion", '--locked-mode')
    Invoke-DotNet @('publish', $project, '-c', 'Release', '--no-restore', '-p:PublishProfile=WindowsPortable', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $publish)

    $auditLines = & $dotnet list DuplicateFinder.slnx package --vulnerable --include-transitive --no-restore --source https://api.nuget.org/v3/index.json --format json --output-version 1
    if ($LASTEXITCODE -ne 0) { throw 'The NuGet vulnerability query did not complete successfully.' }
    $audit = ($auditLines -join [Environment]::NewLine) | ConvertFrom-Json
    if ($audit.version -ne 1 -or @($audit.projects).Count -ne 4 -or @($audit.sources).Count -eq 0) { throw 'The vulnerability report is incomplete.' }
    $auditProblems = @($audit.problems) + @($audit.errors) + @($audit.logs)
    foreach ($item in $audit.projects) { $auditProblems += @($item.problems) + @($item.errors) + @($item.logs) }
    if (@($auditProblems | Where-Object { $null -ne $_ }).Count -gt 0) { throw 'The vulnerability report includes issues that require review.' }
    $vulnerabilities = 0
    foreach ($item in $audit.projects) {
        $prefix = $repoRoot.Replace('\', '/').TrimEnd('/') + '/'
        $fullPath = $item.path.Replace('\', '/')
        if (!$fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The vulnerability report contains an unexpected project.' }
        $item.path = $fullPath.Substring($prefix.Length)
        foreach ($framework in $item.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                $vulnerabilities += @($package.vulnerabilities | Where-Object { $null -ne $_ }).Count
            }
        }
    }
    $auditReport = [pscustomobject]@{ CheckedUtc = [DateTimeOffset]::UtcNow.ToString('o'); ReportedVulnerabilities = $vulnerabilities; NuGetReport = $audit }
    [IO.File]::WriteAllText((Join-Path $publish 'nuget-vulnerability-report.json'), ($auditReport | ConvertTo-Json -Depth 20))
    if ($vulnerabilities -gt 0) { throw 'NuGet reports vulnerable packages. Review the report before producing a ZIP.' }

    & (Join-Path $PSScriptRoot 'Write-DependencyInventory.ps1') -PublishDirectory $publish -AssetsFile (Join-Path $repoRoot 'src/DuplicateFinder.Desktop/obj/project.assets.json')
    foreach ($file in @('DuplicateFinder.Desktop.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll')) {
        $path = Join-Path $publish $file
        if (!(Test-Path -LiteralPath $path)) { throw 'A required Windows runtime/native file is missing.' }
        Assert-X64Image $path
    }
    $unexpected = @(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object {
        $_.Name -match '(?i)^(AvaloniaUI\.|Avalonia\.Diagnostics\.)' -or $_.Extension -in @('.pdb', '.jsonl', '.log') -or $_.Name -like '*.runtimeconfig.dev.json' -or $_.Name -eq 'PROJECT_CONTEXT.md'
    })
    if ($unexpected.Count -gt 0) { throw 'Developer tooling, debug symbols or local data appeared in the publish output.' }
    $config = Get-Content -LiteralPath (Join-Path $publish 'DuplicateFinder.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (!($config.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -eq $runtimeVersion })) {
        throw 'The expected self-contained runtime is missing.'
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/windows-portable-guide.md') -Destination (Join-Path $publish 'USER-GUIDE.md')
    $start = "DuplicateFinder - unsigned Windows x64 local test candidate`r`n`r`nExtract the entire ZIP, then run DuplicateFinder.Desktop.exe.`r`nThe .NET runtime is included. Keep all files together.`r`nStart with copied test files and use quarantine for cleanup.`r`nRead USER-GUIDE.md and the package/review reports before sharing.`r`nNo installation, updater, public release approval or signing is implied.`r`n"
    [IO.File]::WriteAllText((Join-Path $publish 'START-HERE.txt'), $start)
    $packageInfo = [pscustomobject]@{
        Schema = 1; SourceCommit = $commit; PackagingInputsModified = $dirty
        CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o'); Sdk = ((& $dotnet --version) -join '').Trim()
        Runtime = $runtimeVersion; RuntimeIdentifier = 'win-x64'; Configuration = 'Release'; SelfContained = $true
        Signed = $false; Distribution = 'Portable test candidate; Windows 11 x64 initial validation target'
        OpenReleaseGates = @('Application license choice', 'Native/font attribution closure', 'Runtime security release review', 'Clean machine without SDK', 'Accessibility/DPI and storage matrix', 'Initial packaged scan IOException follow-up')
    }
    [IO.File]::WriteAllText((Join-Path $publish 'package-info.json'), ($packageInfo | ConvertTo-Json -Depth 5))
    $manifest = @(Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ File = $_.FullName.Substring($publish.Length + 1).Replace('\', '/'); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    [IO.File]::WriteAllText((Join-Path $publish 'file-manifest.json'), ($manifest | ConvertTo-Json -Depth 4))
    $zip = Join-Path $runRoot ($name + '.zip')
    Compress-Archive -LiteralPath $publish -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($zip + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine)
    if ($env:GITHUB_OUTPUT) {
        [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "zip=$zip`nchecksum=$zip.sha256`n")
    }
    [pscustomobject]@{
        PackageDirectory = $publish; Zip = $zip; SHA256 = $hash
        PublishedBytes = (Get-ChildItem -LiteralPath $publish -File -Recurse | Measure-Object Length -Sum).Sum; ZipBytes = (Get-Item -LiteralPath $zip).Length
    }
}
finally { Pop-Location }
