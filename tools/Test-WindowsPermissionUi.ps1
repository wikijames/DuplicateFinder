param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [switch]$ExerciseFailureCleanup
)
# Only new script-owned fixtures. Never accepts a scan root, account or ACL target.
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT' -or $PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Permission acceptance requires Windows PowerShell 5.1.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$zip = (Resolve-Path -LiteralPath $ZipPath).Path
$verified = & (Join-Path $PSScriptRoot 'Test-WindowsPortable.ps1') -ZipPath $zip
$info = Get-Content -LiteralPath (Join-Path $verified.ExtractedDirectory 'package-info.json') -Raw | ConvertFrom-Json
if ($verified.Result -ne 'PASS' -or $info.PackagingInputsModified -or $info.DistributionPolicy -ne 'Private testing only') {
    throw 'Permission checks require a verified committed private-testing candidate.'
}
$fixture = Join-Path $repoRoot ('local-only/permission-acceptance/' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $fixture) { throw 'Permission fixture must be new.' }
if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($fixture)).DriveFormat -ne 'NTFS') { throw 'This permission check requires a local NTFS fixture.' }
$source = Join-Path $fixture 'source'
$healthy = Join-Path $source 'healthy'
$restricted = Join-Path $source 'restricted'
New-Item -ItemType Directory -Path $healthy, $restricted | Out-Null
foreach ($name in @('first', 'second', 'third')) {
    [IO.File]::WriteAllText((Join-Path $healthy ($name + '.txt')), 'Generated healthy permission-test duplicate')
}
$lockedPath = Join-Path $source 'locked.txt'
[IO.File]::WriteAllText($lockedPath, 'Generated healthy permission-test duplicate')
foreach ($name in @('first', 'second')) {
    [IO.File]::WriteAllText((Join-Path $restricted ($name + '.txt')), 'Generated restricted-folder duplicate')
}
$hashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) { $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$accessSection = [Security.AccessControl.AccessControlSections]::Access
$originalDacl = [IO.Directory]::GetAccessControl($restricted, $accessSection).GetSecurityDescriptorSddlForm($accessSection)
$aclChanged = $false
$aclRestored = $true
$fileLock = $null
$lockReleased = $true
$app = $null
$window = $null
$stage = 'Start'
$failure = $null
$cleanupFailure = $null
$cases = New-Object 'System.Collections.Generic.List[object]'
$probeDenied = $false
$probeLocked = $false
$warningPersistent = $false
$runtimeVerified = $false
$contentsUnchanged = $false

function Assert-RestrictedFixture {
    $full = [IO.Path]::GetFullPath($restricted)
    if (!$full.StartsWith($fixture + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $full -ne (Join-Path $source 'restricted')) { throw 'ACL target escaped the new fixture.' }
    # Reject aliases on every ancestor before changing permissions.
    for ($path = $full; $null -ne $path; $path = [IO.Path]::GetDirectoryName($path)) {
        if (((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Reparse-point ACL targets are not supported.'
        }
    }
}
function Restore-FixtureAccess {
    Assert-RestrictedFixture
    $security = New-Object Security.AccessControl.DirectorySecurity
    # Reconstructing marks the access section modified; persisting a read-only
    # original security object can otherwise be a no-op.
    $security.SetSecurityDescriptorSddlForm($originalDacl, $accessSection)
    [IO.Directory]::SetAccessControl($restricted, $security)
    $script:aclRestored = [IO.Directory]::GetAccessControl($restricted, $accessSection).GetSecurityDescriptorSddlForm($accessSection) -eq $originalDacl
    if (!$script:aclRestored) { throw 'The fixture access descriptor was not restored.' }
    $script:aclChanged = $false
}
function Wait-PermissionUi([scriptblock]$Predicate) {
    for ($attempt = 0; $attempt -lt 200; $attempt++) {
        if (& $Predicate) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Expected permission-test UI state did not appear.'
}
function Find-PermissionControl([string]$Id) {
    $control = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
    if ($null -eq $control) { throw 'Expected permission-test control is unavailable.' }
    return $control
}
function Warning-IsVisible {
    $control = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'ScanWarningText'))
    return $null -ne $control -and !$control.Current.IsOffscreen
}
function Scan-PermissionCase([string]$Name, [int]$Groups, [int]$DuplicateFiles, [int]$SkippedFiles, [int]$SkippedFolders) {
    (Find-PermissionControl ScanButton).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
    Wait-PermissionUi { (Find-PermissionControl ScanButton).Current.IsEnabled }
    $status = (Find-PermissionControl StatusText).Current.Name
    if ((Find-PermissionControl VisibleGroupsText).Current.Name -ne "$Groups of $Groups groups") { throw 'Healthy duplicate groups were not retained.' }
    # The summary number is an inline Run, not a separate automation control.
    $fileTotal = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, "$DuplicateFiles files in groups"))
    if ($null -eq $fileTotal) { throw 'Healthy duplicate file totals were not retained.' }
    $incomplete = $SkippedFiles -gt 0 -or $SkippedFolders -gt 0
    $visible = Warning-IsVisible
    if ($incomplete) {
        if (!$status.StartsWith('Incomplete scan:', [StringComparison]::Ordinal) -or !$visible -or
            !$status.Contains("Skipped $SkippedFiles file(s) and $SkippedFolders folder(s)")) {
            throw 'Incomplete scan did not expose the expected issue counts and warning.'
        }
    }
    elseif ($status.StartsWith('Incomplete scan:', [StringComparison]::Ordinal) -or $visible) {
        throw 'A recovered complete scan retained an incomplete warning.'
    }
    if (!(Find-PermissionControl CleanupButton).Current.IsEnabled) { throw 'Healthy group was not reviewable.' }
    # Read availability only; no cleanup/restore action is invoked by this tool.
    $cases.Add([pscustomobject]@{ Case = $Name; Groups = $Groups; DuplicateFiles = $DuplicateFiles; SkippedFiles = $SkippedFiles; SkippedFolders = $SkippedFolders; WarningVisible = $visible; Passed = $true })
}

try {
    $app = Start-Process -FilePath (Join-Path $verified.ExtractedDirectory 'DuplicateFinder.Desktop.exe') -PassThru -WindowStyle Normal
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'DuplicateFinder'))
    Wait-PermissionUi {
        $script:window = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $condition)
        $null -ne $script:window
    }
    $runtime = $app.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1
    $runtimeVerified = $null -ne $runtime -and [string]::Equals($runtime.FileName, (Join-Path $verified.ExtractedDirectory 'coreclr.dll'), [StringComparison]::OrdinalIgnoreCase)
    if (!$runtimeVerified) { throw 'Permission-test app did not load its bundled runtime.' }
    (Find-PermissionControl RootPathBox).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($source)
    $stage = 'AccessibleBaseline'
    Scan-PermissionCase $stage 2 6 0 0

    $stage = 'DeniedFolder'
    Assert-RestrictedFixture
    $denied = New-Object Security.AccessControl.DirectorySecurity
    $denied.SetSecurityDescriptorSddlForm($originalDacl, $accessSection)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($identity.User, [Security.AccessControl.FileSystemRights]::ListDirectory,
            [Security.AccessControl.InheritanceFlags]::None, [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Deny)
        $denied.AddAccessRule($rule)
    }
    finally { $identity.Dispose() }
    $aclChanged = $true; $aclRestored = $false
    [IO.Directory]::SetAccessControl($restricted, $denied)
    try { [IO.Directory]::GetFiles($restricted) | Out-Null }
    catch [UnauthorizedAccessException] { $probeDenied = $true }
    if (!$probeDenied) { throw 'The test folder denial was not effective.' }
    Scan-PermissionCase $stage 1 4 0 1

    $stage = 'DeniedFolderAndLockedFile'
    $fileLock = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $lockReleased = $false
    try { $probe = [IO.File]::OpenRead($lockedPath); $probe.Dispose() }
    catch [IO.IOException] { $probeLocked = $true }
    if (!$probeLocked) { throw 'The test sharing restriction was not effective.' }
    if ($ExerciseFailureCleanup) { $stage = 'InjectedFailureWithActiveRestrictions'; throw 'Deliberate fixture cleanup-path failure.' }
    Scan-PermissionCase $stage 1 3 1 1
    (Find-PermissionControl ResultsSearchBox).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('generated-no-match')
    Start-Sleep -Milliseconds 150
    $warningPersistent = (Warning-IsVisible) -and !(Find-PermissionControl CleanupButton).Current.IsEnabled
    if (!$warningPersistent) { throw 'Filtering hid the incomplete-scan notice or retained hidden cleanup eligibility.' }
    (Find-PermissionControl ResultsSearchBox).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')

    $stage = 'FolderAccessRestoredFileStillLocked'
    Restore-FixtureAccess
    if (@([IO.Directory]::GetFiles($restricted)).Count -ne 2) { throw 'Restored folder is not readable.' }
    Scan-PermissionCase $stage 2 5 1 0

    $stage = 'AllAccessRestored'
    $fileLock.Dispose(); $fileLock = $null; $lockReleased = $true
    Scan-PermissionCase $stage 2 6 0 0
}
catch {
    $code = switch -Exact ($_.Exception.Message) {
        'Expected permission-test control is unavailable.' { 'MissingControl' }
        'Healthy duplicate groups were not retained.' { 'GroupTotals' }
        'Healthy duplicate file totals were not retained.' { 'FileTotals' }
        'Incomplete scan did not expose the expected issue counts and warning.' { 'IssueCountsOrWarning' }
        'A recovered complete scan retained an incomplete warning.' { 'StaleWarning' }
        'Healthy group was not reviewable.' { 'HealthyReview' }
        'Deliberate fixture cleanup-path failure.' { 'InjectedFailure' }
        default { 'UiOrFixtureCheck' }
    }
    $failure = [pscustomobject]@{ Stage = $stage; Type = $_.Exception.GetType().Name; CheckCode = $code }
}
finally {
    try {
        if ($null -ne $fileLock) { $fileLock.Dispose(); $fileLock = $null; $lockReleased = $true }
        if ($aclChanged) { Restore-FixtureAccess }
        $aclRestored = [IO.Directory]::GetAccessControl($restricted, $accessSection).GetSecurityDescriptorSddlForm($accessSection) -eq $originalDacl
        foreach ($path in $hashes.Keys) {
            if ((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]) { throw 'Generated fixture content changed.' }
        }
        $contentsUnchanged = $true
    }
    catch { $cleanupFailure = $_.Exception.GetType().Name }
    finally {
        if ($null -ne $app) {
            try {
                if (!$app.HasExited) { $app.CloseMainWindow() | Out-Null; if (!$app.WaitForExit(3000)) { $app.Kill(); $app.WaitForExit(3000) | Out-Null } }
            }
            finally { $app.Dispose() }
        }
    }
}
$passed = $null -eq $failure -and $null -eq $cleanupFailure -and $aclRestored -and $lockReleased -and $cases.Count -eq 5
$report = [pscustomobject]@{
    Schema = 1; CheckedUtc = [DateTimeOffset]::UtcNow.ToString('o'); Passed = $passed
    SourceCommit = $verified.SourceCommit; PackageSHA256 = (Get-FileHash -LiteralPath $zip).Hash
    HarnessSHA256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash
    FileSystem = 'NTFS'; Cases = $cases.ToArray(); Failure = $failure; CleanupFailureType = $cleanupFailure
    FolderDenialVerified = $probeDenied; ExclusiveLockVerified = $probeLocked; WarningPersistedDuringFiltering = $warningPersistent
    OriginalFolderAccessRestored = $aclRestored; ExclusiveLockReleased = $lockReleased
    FixtureContentsUnchanged = $contentsUnchanged
    BundledRuntimeVerified = $runtimeVerified; FailureCleanupExercised = [bool]$ExerciseFailureCleanup
    SyntheticFilesOnly = $true; ConfirmedCleanup = $false; CleanMachineValidated = $false
}
$reportPath = Join-Path $fixture 'permission-ui-report.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 7))
if (!$passed) { throw 'Permission UI acceptance failed; inspect the local sanitized permission-ui-report.json.' }
[pscustomobject]@{ PermissionChecksPassed = $true; SourceCommit = $verified.SourceCommit; ReportPath = $reportPath }
# No fixture purge. ACL/account identifiers and raw descriptors remain in memory only.
