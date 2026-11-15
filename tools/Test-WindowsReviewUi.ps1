# Run after a Windows x64 Release build. Uses only a new app process and generated fixtures.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SyntheticReviewLinks {
    [DllImport("kernel32.dll", EntryPoint="CreateHardLinkW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Create(string alias, string original, IntPtr security);
}
'@
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/DuplicateFinder.Desktop/bin/Release/net10.0/DuplicateFinder.Desktop.exe'))
if (!(Test-Path -LiteralPath $executable)) { throw 'Build the Windows Release desktop app before running this smoke test.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('DuplicateFinder-ReviewSmoke-' + [guid]::NewGuid().ToString('N'))
$small = Join-Path $fixture 'small'
foreach ($name in @('original', 'copy-one', 'copy-two')) {
    $folder = Join-Path $small $name
    New-Item -ItemType Directory -Path $folder | Out-Null
    [IO.File]::WriteAllText((Join-Path $folder 'same.txt'), 'Synthetic review smoke fixture')
}
$testProcess = Start-Process -FilePath $executable -PassThru -WindowStyle Normal
$desktop = [Windows.Automation.AutomationElement]::RootElement
$script:reviewWindow = $null

function Wait-Until([scriptblock]$Predicate, [string]$Message) {
    for ($attempt = 0; $attempt -lt 200; $attempt++) {
        if (& $Predicate) { return }
        Start-Sleep -Milliseconds 100
    }
    throw $Message
}
function Find-Control([string]$Id) {
    $control = $script:reviewWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
    if ($null -eq $control) { throw "Missing UI control: $Id" }
    return $control
}
function Invoke-Control([string]$Id) {
    (Find-Control $Id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
}
function Set-Text([string]$Id, [string]$Value) {
    (Find-Control $Id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
    Start-Sleep -Milliseconds 150
}
function Select-Choice([string]$Id, [string]$Text) {
    $combo = Find-Control $Id
    $expand = $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 150
    $items = $combo.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))
    $choice = $items | Where-Object { $_.Current.Name.IndexOf($Text, [StringComparison]::OrdinalIgnoreCase) -ge 0 } | Select-Object -First 1
    if ($null -eq $choice) { throw "Missing choice in $Id" }
    $choice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
    Start-Sleep -Milliseconds 200
}
function Assert-ProtectedFile {
    $path = $script:reviewWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Protected file; will be kept'))
    if ($null -eq $path -or $path.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne (Join-Path $small 'copy-two/same.txt')) {
        throw 'Confirmation did not preserve the selected keep file.'
    }
    if (!(Find-Control CancelCleanupButton).Current.HasKeyboardFocus) { throw 'Cancel must have initial confirmation focus.' }
}

try {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $testProcess.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'DuplicateFinder'))
    Wait-Until { $script:reviewWindow = $desktop.FindFirst([Windows.Automation.TreeScope]::Children, $condition); $null -ne $script:reviewWindow } 'Test app did not open.'
    Set-Text RootPathBox $small
    Invoke-Control ScanButton
    Wait-Until { (Find-Control ScanButton).Current.IsEnabled } 'Synthetic scan did not finish.'
    if ((Find-Control VisibleGroupsText).Current.Name -ne '1 of 1 groups') { throw 'Expected one three-copy group.' }
    $script:reviewWindow.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize(920, 660)
    Start-Sleep -Milliseconds 200
    foreach ($id in @('ResultsList', 'GroupFilesList', 'KeepFileComboBox', 'CleanupButton', 'PreviewFileButton')) {
        $control = Find-Control $id
        $height = $control.Current.BoundingRectangle.Height
        if ([double]::IsInfinity($height) -or $height -lt 25 -or $control.Current.IsOffscreen) { throw "Clipped minimum-size control: $id" }
    }
    Select-Choice KeepFileComboBox 'copy-two'
    Invoke-Control CleanupButton
    Assert-ProtectedFile
    Invoke-Control CancelCleanupButton
    if (!(Find-Control CleanupButton).Current.HasKeyboardFocus) { throw 'Focus did not return after cancellation.' }
    Select-Choice CleanupActionBox 'Permanently delete'
    Invoke-Control CleanupButton
    Assert-ProtectedFile
    $dialog = $script:reviewWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Confirm duplicate cleanup'))
    $dialog.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize(480, 360)
    if ((Find-Control ConfirmCleanupButton).Current.IsOffscreen) { throw 'Confirmation action clipped at minimum dialog size.' }
    Invoke-Control CancelCleanupButton
    if ((Get-ChildItem -LiteralPath $small -File -Recurse).Count -ne 3) { throw 'Cancellation changed fixture files.' }
    Select-Choice CleanupActionBox 'Quarantine'

    $files = (Find-Control GroupFilesList).FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))
    if ($files.Count -eq 0) { throw 'File rows missing.' }
    $files[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control PreviewFileButton
    $preview = $script:reviewWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Read-only file preview'))
    if ($preview.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'Synthetic review smoke fixture') { throw 'Preview content changed.' }
    $dialog = $script:reviewWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'File preview'))
    $dialog.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 200
    Invoke-Control CleanupButton
    Assert-ProtectedFile
    Invoke-Control CancelCleanupButton

    Set-Text ResultsSearchBox 'synthetic-no-match'
    if ((Find-Control CleanupButton).Current.IsEnabled) { throw 'Hidden group retained cleanup eligibility.' }
    Set-Text ResultsSearchBox ''
    if ((Find-Control VisibleGroupsText).Current.Name -ne '1 of 1 groups') { throw 'Clear search did not restore results.' }
    $groups = (Find-Control ResultsList).FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))
    $groups[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control CleanupButton
    Assert-ProtectedFile
    Invoke-Control CancelCleanupButton
    foreach ($theme in @('Light', 'Dark', 'System')) { Select-Choice AppearanceBox $theme }
    (Find-Control RootPathBox).SetFocus()
    $keys = New-Object -ComObject WScript.Shell
    $keys.SendKeys('^f')
    Start-Sleep -Milliseconds 150
    if (!(Find-Control ResultsSearchBox).Current.HasKeyboardFocus) { throw 'Ctrl+F did not focus search.' }
    $keys.SendKeys('{TAB}')
    Start-Sleep -Milliseconds 100
    if (!(Find-Control ResultsSortBox).Current.HasKeyboardFocus) { throw 'Tab did not move from search to sort.' }
    $keys.SendKeys('+{TAB}')
    Start-Sleep -Milliseconds 100
    if (!(Find-Control ResultsSearchBox).Current.HasKeyboardFocus) { throw 'Shift+Tab did not return to search.' }

    Invoke-Control ScanSettingsButton
    $dialog = $script:reviewWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Scan settings'))
    $dialog.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize(480, 360)
    $apply = $dialog.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Apply settings'))
    if ($apply.Current.IsOffscreen) { throw 'Settings actions clipped at compact dialog size.' }
    Set-Text MinimumSizeBox '2'
    Set-Text MaximumSizeBox '1'
    $apply.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
    $cancel = $dialog.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Cancel'))
    if ($null -eq $cancel) { throw 'Invalid settings unexpectedly closed the dialog.' }
    $cancel.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
    if (!(Find-Control ScanSettingsButton).Current.HasKeyboardFocus) { throw 'Settings focus did not return.' }
    Invoke-Control DiagnosticsButton
    $dialog = $script:reviewWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Local diagnostics'))
    $dialog.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize(480, 360)
    if ((Find-Control StartDiagnosticsButton).Current.IsOffscreen) { throw 'Diagnostics actions clipped at compact dialog size.' }
    $close = $dialog.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Close'))
    $close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
    if (!(Find-Control DiagnosticsButton).Current.HasKeyboardFocus) { throw 'Diagnostics focus did not return.' }

    $large = Join-Path $fixture 'many-groups'
    New-Item -ItemType Directory -Path $large | Out-Null
    for ($group = 0; $group -lt 300; $group++) {
        foreach ($copy in @('first', 'second')) {
            [IO.File]::WriteAllText((Join-Path $large ("group-{0:D4}-{1}.txt" -f $group, $copy)), ("Synthetic group {0:D4} " -f $group) + ('x' * 100))
        }
    }
    if (![SyntheticReviewLinks]::Create((Join-Path $small 'original/alias.txt'), (Join-Path $small 'original/same.txt'), [IntPtr]::Zero)) {
        throw 'Could not create the synthetic warning fixture.'
    }
    Set-Text RootPathBox $fixture
    Invoke-Control ScanButton
    Wait-Until { (Find-Control ScanButton).Current.IsEnabled } 'Large synthetic result set did not finish.'
    if ((Find-Control VisibleGroupsText).Current.Name -ne '301 of 301 groups') { throw 'Large synthetic result count incorrect.' }
    if (!(Find-Control CleanupButton).Current.IsEnabled) { throw 'Large result set is not reviewable.' }
    if ((Find-Control ScanWarningText).Current.IsOffscreen -or !(Find-Control ScanWarningText).Current.Name.Contains('hard-linked')) { throw 'Hard-link notice missing.' }
    foreach ($id in @('ResultsList', 'GroupFilesList', 'CleanupButton')) {
        $control = Find-Control $id
        if ($control.Current.IsOffscreen -or $control.Current.BoundingRectangle.Height -lt 25) { throw 'Persistent notice clipped the review workspace.' }
    }
    'PASS: keep choice, preview-only selection, both cancellations, dialog focus/resizing, invalid settings, diagnostics defaults, search safety, themes, keyboard traversal, persistent warning and 301 synthetic groups.'
}
finally {
    if ($null -ne $script:reviewWindow) {
        $script:reviewWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    }
    elseif (!$testProcess.HasExited) { $testProcess.CloseMainWindow() | Out-Null }
    $testProcess.Dispose()
    # Deliberately retain generated fixtures for inspection; never remove user data.
}
