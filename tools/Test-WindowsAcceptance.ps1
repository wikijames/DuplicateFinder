param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [ValidateRange(1, 5)][int]$Repetitions = 3
)
# Execute only candidates built from reviewed DuplicateFinder source.
# Windows PowerShell 5.1 compatible; this runner does not invoke the SDK.
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows acceptance checks require Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$zip = (Resolve-Path -LiteralPath $ZipPath).Path
$verified = & (Join-Path $PSScriptRoot 'Test-WindowsPortable.ps1') -ZipPath $zip
if ($verified.Result -ne 'PASS') { throw 'The archive verification did not pass.' }
$info = Get-Content -LiteralPath (Join-Path $verified.ExtractedDirectory 'package-info.json') -Raw | ConvertFrom-Json
if ($info.PackagingInputsModified) { throw 'Acceptance requires a candidate built from committed packaging inputs.' }
$runRoot = Join-Path $repoRoot ('local-only/acceptance/' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$emptyRuntime = Join-Path $runRoot 'empty-runtime'
New-Item -ItemType Directory -Path $emptyRuntime | Out-Null
$reportPath = Join-Path $runRoot 'acceptance-report.json'
$variables = @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_X86', 'DOTNET_MULTILEVEL_LOOKUP')
$previous = @{}
$runs = New-Object 'System.Collections.Generic.List[object]'
try {
    foreach ($name in $variables) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        $value = if ($name -eq 'DOTNET_MULTILEVEL_LOOKUP') { '0' } else { $emptyRuntime }
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
    for ($iteration = 1; $iteration -le $Repetitions; $iteration++) {
        Write-Host "Running fresh-process UI acceptance $iteration of $Repetitions..."
        try {
            $output = @(& (Join-Path $PSScriptRoot 'Test-WindowsReviewUi.ps1') -ExecutablePath (Join-Path $verified.ExtractedDirectory 'DuplicateFinder.Desktop.exe') -PassThru)
            $metrics = @($output | Where-Object { $null -ne $_.PSObject.Properties['UiChecksPassed'] })
            if ($metrics.Count -ne 1 -or !$metrics[0].UiChecksPassed -or !$metrics[0].BundledRuntimeVerified) {
                throw 'UI checks did not return complete bundled-runtime evidence.'
            }
            $runs.Add([pscustomobject]@{
                Iteration = $iteration; Passed = $true; FailureType = $null
                BundledRuntimeVerified = $true
                ElapsedSeconds = $metrics[0].ElapsedSeconds
                ProcessLifetimePeakWorkingSetBytes = $metrics[0].ProcessLifetimePeakWorkingSetBytes
                ProcessCpuMilliseconds = $metrics[0].ProcessCpuMilliseconds
            })
        }
        catch {
            # Never save exception messages, stdout/stderr, fixture paths or stack traces.
            $runs.Add([pscustomobject]@{ Iteration = $iteration; Passed = $false; FailureType = $_.Exception.GetType().Name })
            Write-Host "Acceptance iteration $iteration failed; sanitized status will be retained."
        }
    }
}
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
$passed = $runs.Count -eq $Repetitions -and @($runs | Where-Object { !$_.Passed }).Count -eq 0
$harness = @('Test-WindowsAcceptance.ps1', 'Test-WindowsPortable.ps1', 'Test-WindowsReviewUi.ps1') | ForEach-Object {
    [pscustomobject]@{ File = $_; SHA256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash }
}
$windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$report = [pscustomobject]@{
    Schema = 1; CheckedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    SourceCommit = $verified.SourceCommit; PackageSHA256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    VerifiedFiles = $verified.VerifiedFiles; Repetitions = $Repetitions; Passed = $passed
    Harness = @($harness); WindowsBuild = [string]$windows.CurrentBuildNumber; WindowsUpdateRevision = $windows.UBR
    RuntimeDiscoveryEnvironmentOverridden = $true
    CleanMachineValidated = $false
    Runs = $runs.ToArray()
    RemainingManualGates = @('Separate clean Windows machine without SDK', 'Narrator and high contrast',
        '125-200 percent display scaling', 'Native Explorer folder drag/drop', 'Real storage and permission failure matrix',
        'Physical SSD/HDD performance', 'Historical intermittent scan IO failure investigation')
}
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8))
if (!$passed) { throw 'Windows acceptance failed. See the local sanitized acceptance-report.json.' }
[pscustomobject]@{ Result = 'PASS'; Repetitions = $Repetitions; SourceCommit = $verified.SourceCommit; ReportPath = $reportPath }
# Keep the extraction, empty-runtime directory and synthetic fixtures for inspection.
