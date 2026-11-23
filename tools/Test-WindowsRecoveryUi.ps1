param([Parameter(Mandatory = $true)][string]$ZipPath)
# Executes only a reviewed DuplicateFinder candidate. Confirms quarantine/restore
# exclusively for this script's new fixture. No arbitrary scan/destination input.
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Recovery UI acceptance requires Windows.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$zip = (Resolve-Path -LiteralPath $ZipPath).Path
$verified = & (Join-Path $PSScriptRoot 'Test-WindowsPortable.ps1') -ZipPath $zip
$info = Get-Content -LiteralPath (Join-Path $verified.ExtractedDirectory 'package-info.json') -Raw | ConvertFrom-Json
if ($verified.Result -ne 'PASS' -or $info.PackagingInputsModified -or $info.DistributionPolicy -ne 'Private testing only') {
    throw 'Recovery acceptance requires a verified, committed private-testing candidate.'
}
$executable = Join-Path $verified.ExtractedDirectory 'DuplicateFinder.Desktop.exe'
$fixture = Join-Path $repoRoot ('local-only/recovery-acceptance/' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $fixture) { throw 'The recovery fixture must be new.' }
$source = Join-Path $fixture 'source'
$destination = Join-Path $fixture 'destination'
New-Item -ItemType Directory -Path $destination | Out-Null
$originals = @()
foreach ($name in @('original', 'copy-one', 'copy-two')) {
    $folder = Join-Path $source $name
    New-Item -ItemType Directory -Path $folder | Out-Null
    $path = Join-Path $folder 'same.txt'
    [IO.File]::WriteAllText($path, 'Generated recovery acceptance duplicate payload')
    $originals += $path
}
$keep = $originals[2]
$targets = @($originals[0], $originals[1])
$sentinel = Join-Path $source 'unrelated.txt'
[IO.File]::WriteAllText($sentinel, 'Generated unrelated file that must remain untouched')
$hashes = @{}
foreach ($path in $originals + @($sentinel)) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
$script:recoveryWindow = $null
$script:recoveryProcess = $null
$stage = 'Start'
$checks = New-Object 'System.Collections.Generic.List[string]'
$failure = $null
$bundledRuntimeVerified = $false
$started = [Diagnostics.Stopwatch]::StartNew()

function Assert-FixturePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($fixture + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Recovery operation escaped its generated fixture.'
    }
}
function Wait-Recovery([scriptblock]$Predicate) {
    for ($attempt = 0; $attempt -lt 200; $attempt++) {
        if (& $Predicate) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'The expected recovery UI state did not appear.'
}
function Find-RecoveryControl([string]$Id) {
    $control = $script:recoveryWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
    if ($null -eq $control) { throw 'A recovery control is unavailable.' }
    return $control
}
function Find-RecoveryNamed([string]$Name, $Parent = $script:recoveryWindow) {
    $control = $Parent.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name))
    if ($null -eq $control) {
        $script:missingControl = if ($Name -in @('Protected file; will be kept', 'All removal targets for this operation', 'Select recovery batch', 'Review restore', 'Confirm batch restore', 'Cancel', 'Restore files')) { $Name } else { 'Other expected control' }
        throw 'A named recovery control is unavailable.'
    }
    return $control
}
function Invoke-Recovery($Control) {
    $Control.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
}
function Set-RecoveryText([string]$Id, [string]$Value) {
    (Find-RecoveryControl $Id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
    Start-Sleep -Milliseconds 100
}
function Assert-Unchanged([string]$Path) {
    Assert-FixturePath $Path
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-FileHash -LiteralPath $Path).Hash -ne $hashes[$Path]) {
        throw 'A generated source or protected file changed unexpectedly.'
    }
}
function Stop-RecoveryApp {
    if ($null -eq $script:recoveryProcess) { return }
    try {
        if (!$script:recoveryProcess.HasExited) {
            $script:recoveryProcess.CloseMainWindow() | Out-Null
            if (!$script:recoveryProcess.WaitForExit(3000)) {
                # Only our own new app process; retain every fixture/recovery file.
                $script:recoveryProcess.Kill()
                $script:recoveryProcess.WaitForExit(3000) | Out-Null
            }
        }
    }
    finally { $script:recoveryProcess.Dispose(); $script:recoveryProcess = $null; $script:recoveryWindow = $null }
}
function Start-RecoveryApp {
    $script:recoveryProcess = Start-Process -FilePath $executable -PassThru -WindowStyle Normal
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $script:recoveryProcess.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'DuplicateFinder'))
    Wait-Recovery {
        $script:recoveryWindow = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $condition)
        $null -ne $script:recoveryWindow
    }
    $runtime = $script:recoveryProcess.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1
    if ($null -eq $runtime -or ![string]::Equals($runtime.FileName, (Join-Path $verified.ExtractedDirectory 'coreclr.dll'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Recovery app did not load its bundled runtime.'
    }
}
function Assert-ReviewedTargets($Dialog) {
    $scrolls = $Dialog.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true))
    foreach ($control in $scrolls) {
        $scroll = $control.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 150; break }
    }
    $elements = $Dialog.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $elements) {
        # Avalonia may expose SelectableTextBlock's content as its accessible name
        # instead of the attached label. Accept either full text channel, never a subset.
        $text = $element.Current.Name
        if ($text -eq 'All removal targets for this operation') {
            $pattern = $null
            if (!$element.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern, [ref]$pattern)) { continue }
            $text = $pattern.DocumentRange.GetText(-1)
        }
        $displayed = @($text -split '[\r\n]+' | Where-Object { $_.Length -gt 0 })
        if ($displayed.Count -eq 2 -and @($displayed | Select-Object -Unique).Count -eq 2 -and
            @($displayed | Where-Object { $targets -notcontains $_ }).Count -eq 0) { return }
    }
    throw 'Review does not show exactly the generated original paths.'
}
function Assert-QuarantineReview {
    Assert-FixturePath $source; Assert-FixturePath $destination
    foreach ($path in $originals + @($sentinel)) { Assert-Unchanged $path }
    $button = Find-RecoveryControl ConfirmCleanupButton
    if ($button.Current.Name -ne 'Move to quarantine') { throw 'Only quarantine may be confirmed.' }
    $protected = Find-RecoveryNamed 'Protected file; will be kept'
    if ($protected.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $keep) {
        throw 'The nondefault keep choice was not preserved.'
    }
    # Require the complete displayed removal list before allowing a confirmed move.
    Assert-ReviewedTargets (Find-RecoveryNamed 'Confirm duplicate cleanup')
    Set-RecoveryText QuarantineDestinationBox $destination
}
function Review-RecoveryBatch([string]$Batch) {
    Assert-FixturePath $Batch
    Invoke-Recovery (Find-RecoveryControl RestoreBatchButton)
    Set-RecoveryText RestoreBatchPathBox $Batch
    Invoke-Recovery (Find-RecoveryNamed 'Review restore' (Find-RecoveryNamed 'Select recovery batch'))
    Wait-Recovery { $null -ne $script:recoveryWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Confirm batch restore')) }
    $dialog = Find-RecoveryNamed 'Confirm batch restore'
    if (!(Find-RecoveryNamed 'Cancel' $dialog).Current.HasKeyboardFocus) { throw 'Restore Cancel must have initial focus.' }
    Assert-ReviewedTargets $dialog
    return $dialog
}
function Wait-RecoveryIdle {
    Wait-Recovery { (Find-RecoveryControl ScanButton).Current.IsEnabled }
}

try {
    Start-RecoveryApp
    $bundledRuntimeVerified = $true
    $stage = 'ScanAndKeepChoice'
    Set-RecoveryText RootPathBox $source
    Invoke-Recovery (Find-RecoveryControl ScanButton)
    Wait-RecoveryIdle
    if ((Find-RecoveryControl VisibleGroupsText).Current.Name -ne '1 of 1 groups') { throw 'Expected exactly one generated duplicate group.' }
    $combo = Find-RecoveryControl KeepFileComboBox
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 100
    $choices = $combo.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))
    $choice = $choices | Where-Object { $_.Current.Name.IndexOf('copy-two', [StringComparison]::OrdinalIgnoreCase) -ge 0 } | Select-Object -First 1
    if ($null -eq $choice) { throw 'Generated keep choice was not found.' }
    $choice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    $checks.Add($stage)

    $stage = 'QuarantineCancellation'
    Invoke-Recovery (Find-RecoveryControl CleanupButton)
    Assert-QuarantineReview
    Invoke-Recovery (Find-RecoveryControl CancelCleanupButton)
    foreach ($path in $originals + @($sentinel)) { Assert-Unchanged $path }
    if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) { throw 'Cancelled quarantine created recovery data.' }
    $checks.Add($stage)

    $stage = 'QuarantineExecution'
    Invoke-Recovery (Find-RecoveryControl CleanupButton)
    Assert-QuarantineReview
    Invoke-Recovery (Find-RecoveryControl ConfirmCleanupButton)
    Wait-RecoveryIdle
    if ((Find-RecoveryControl StatusText).Current.Name -notlike 'Quarantined 2 file(s).*') { throw 'Quarantine did not report two moved files.' }
    Assert-Unchanged $keep; Assert-Unchanged $sentinel
    if (@($targets | Where-Object { Test-Path -LiteralPath $_ }).Count -ne 0) { throw 'Quarantined targets still exist at source.' }
    $quarantineRoot = Join-Path $destination '.DuplicateFinder-Quarantine'
    $batches = @(Get-ChildItem -LiteralPath $quarantineRoot -Directory -Force)
    if ($batches.Count -ne 1) { throw 'Expected one generated recovery batch.' }
    $batch = $batches[0].FullName
    Assert-FixturePath $batch
    $manifestPath = Join-Path $batch 'recovery.manifest'
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath).Hash
    $payloads = @(Get-ChildItem -LiteralPath $batch -File -Filter '*.bin')
    if ($payloads.Count -ne 2) { throw 'Expected two quarantined payloads.' }
    foreach ($payload in $payloads) {
        Assert-FixturePath $payload.FullName
        if ((Get-FileHash -LiteralPath $payload.FullName).Hash -ne $hashes[$targets[0]]) { throw 'Quarantined payload content changed.' }
    }
    $checks.Add($stage)

    $stage = 'RestartAndRestoreCancellation'
    Stop-RecoveryApp
    Start-RecoveryApp
    $dialog = Review-RecoveryBatch $batch
    Invoke-Recovery (Find-RecoveryNamed 'Cancel' $dialog)
    Wait-RecoveryIdle
    if (@($targets | Where-Object { Test-Path -LiteralPath $_ }).Count -ne 0 -or
        @(Get-ChildItem -LiteralPath $batch -File -Filter '*.bin').Count -ne 2) { throw 'Cancelled restore changed fixture state.' }
    Assert-Unchanged $keep; Assert-Unchanged $sentinel
    if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne $manifestHash) { throw 'Cancelled restore changed the manifest.' }
    $checks.Add($stage)

    $stage = 'RestoreConflictProtection'
    $dialog = Review-RecoveryBatch $batch
    $conflict = $targets[0]
    Assert-FixturePath $conflict
    [IO.File]::WriteAllText($conflict, 'Generated conflicting file; must not be overwritten')
    $conflictHash = (Get-FileHash -LiteralPath $conflict).Hash
    Invoke-Recovery (Find-RecoveryNamed 'Restore files' $dialog)
    Wait-RecoveryIdle
    if ((Find-RecoveryControl StatusText).Current.Name -notlike 'Restore stopped.*' -or
        (Get-FileHash -LiteralPath $conflict).Hash -ne $conflictHash -or
        @(Get-ChildItem -LiteralPath $batch -File -Filter '*.bin').Count -ne 2) { throw 'Restore conflict protection failed.' }
    Assert-Unchanged $keep; Assert-Unchanged $sentinel
    if (Test-Path -LiteralPath $targets[1]) { throw 'Conflict preflight moved another target.' }
    foreach ($payload in $payloads) {
        if ((Get-FileHash -LiteralPath $payload.FullName).Hash -ne $hashes[$targets[0]]) { throw 'Conflict handling changed a payload.' }
    }
    if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne $manifestHash) { throw 'Conflict handling changed the manifest.' }
    $preservedConflict = Join-Path $fixture 'preserved-conflict.txt'
    Assert-FixturePath $conflict; Assert-FixturePath $preservedConflict
    if (Test-Path -LiteralPath $preservedConflict) { throw 'Conflict evidence destination must be new.' }
    Move-Item -LiteralPath $conflict -Destination $preservedConflict
    $checks.Add($stage)

    $stage = 'ConfirmedRestore'
    $dialog = Review-RecoveryBatch $batch
    Invoke-Recovery (Find-RecoveryNamed 'Restore files' $dialog)
    Wait-RecoveryIdle
    if ((Find-RecoveryControl StatusText).Current.Name -notlike 'Restored 2 file(s);*') { throw 'Restore did not report two recovered files.' }
    foreach ($path in $originals + @($sentinel)) { Assert-Unchanged $path }
    if (@(Get-ChildItem -LiteralPath $batch -File -Filter '*.bin').Count -ne 0 -or
        (Get-FileHash -LiteralPath $manifestPath).Hash -ne $manifestHash) { throw 'Payload/manifest state is incorrect after restore.' }
    $checks.Add($stage)

    $stage = 'RepeatRestore'
    $dialog = Review-RecoveryBatch $batch
    Invoke-Recovery (Find-RecoveryNamed 'Restore files' $dialog)
    Wait-RecoveryIdle
    if ((Find-RecoveryControl StatusText).Current.Name -notlike 'Restored 0 file(s);*') { throw 'Repeat restore was not safely repeatable.' }
    foreach ($path in $originals + @($sentinel)) { Assert-Unchanged $path }
    if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne $manifestHash) { throw 'Repeat restore changed the manifest.' }
    $checks.Add($stage)
}
catch {
    # Map only our fixed assertions to codes; never retain arbitrary exception text.
    $code = switch -Exact ($_.Exception.Message) {
        'Only quarantine may be confirmed.' { 'QuarantineActionLabel' }
        'The nondefault keep choice was not preserved.' { 'ProtectedKeepChoice' }
        'Review does not show exactly the generated original paths.' { 'ReviewedPathList' }
        'Cancelled quarantine created recovery data.' { 'CancellationSideEffects' }
        'A recovery control is unavailable.' { 'MissingControlId' }
        'A named recovery control is unavailable.' { 'MissingNamedControl' }
        'Recovery operation escaped its generated fixture.' { 'FixtureBoundary' }
        'A generated source or protected file changed unexpectedly.' { 'SourceHash' }
        'Restore did not report two recovered files.' { 'RestoreResultStatus' }
        'Repeat restore was not safely repeatable.' { 'RepeatRestoreResultStatus' }
        default { 'UiOrFixtureCheck' }
    }
    $failure = [pscustomobject]@{ Stage = $stage; Type = $_.Exception.GetType().Name; CheckCode = $code; MissingKnownControl = $script:missingControl }
}
finally { Stop-RecoveryApp }

$passed = $null -eq $failure -and $checks.Count -eq 7
$report = [pscustomobject]@{
    Schema = 1; CheckedUtc = [DateTimeOffset]::UtcNow.ToString('o'); Passed = $passed
    SourceCommit = $verified.SourceCommit; PackageSHA256 = (Get-FileHash -LiteralPath $zip).Hash
    HarnessSHA256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash
    BundledRuntimeVerified = $bundledRuntimeVerified; CompletedChecks = $checks.ToArray(); Failure = $failure
    ElapsedSeconds = [Math]::Round($started.Elapsed.TotalSeconds, 2)
    SyntheticFilesOnly = $true; ConfirmedPermanentDeletion = $false; CleanMachineValidated = $false
}
$reportPath = Join-Path $fixture 'recovery-ui-report.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 6))
if (!$passed) { throw 'Recovery UI acceptance failed; inspect the local sanitized recovery-ui-report.json.' }
[pscustomobject]@{ RecoveryChecksPassed = $true; SourceCommit = $verified.SourceCommit; ReportPath = $reportPath }
# Retain all generated files, preserved conflict evidence and the immutable batch manifest.
