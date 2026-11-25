param([string]$ExecutablePath)
# Use only an executable built from reviewed DuplicateFinder source.
# Creates temporary drive aliases backed exclusively by generated fixtures.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SyntheticDriveAlias {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern uint QueryDosDevice(string device, StringBuilder target, int size);
    public static string Target(string device) {
        var target = new StringBuilder(32768);
        if (QueryDosDevice(device, target, target.Capacity) != 0) return target.ToString();
        if (Marshal.GetLastWin32Error() == 2) return null;
        throw new InvalidOperationException("Cannot safely inspect drive alias.");
    }
}
'@
$executable = if ($ExecutablePath) { [IO.Path]::GetFullPath($ExecutablePath) } else {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/DuplicateFinder.Desktop/bin/Release/net10.0/DuplicateFinder.Desktop.exe'))
}
if (!(Test-Path -LiteralPath $executable)) { throw 'Build the Windows Release app first.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('DuplicateFinder-DriveSmoke-' + [guid]::NewGuid().ToString('N'))
$aliases = @()
$testProcess = $null
$script:driveWindow = $null
function Find-Control([string]$Id) {
    $control = $script:driveWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
    if ($null -eq $control) { throw "Missing test control: $Id" }
    return $control
}
function Invoke-Control([string]$Id) {
    (Find-Control $Id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
}
function Wait-Until([scriptblock]$Predicate) {
    for ($attempt = 0; $attempt -lt 200; $attempt++) {
        if (& $Predicate) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Synthetic drive UI check timed out.'
}
try {
    foreach ($index in 0..1) {
        $source = [IO.Directory]::CreateDirectory((Join-Path $fixture "source-$index")).FullName
        $visible = [IO.Directory]::CreateDirectory((Join-Path $source 'visible')).FullName
        # One copy per drive: a result is possible only when the drives are compared together.
        [IO.File]::WriteAllText((Join-Path $visible "copy-$index.png"), 'Synthetic exact image-category fixture; not decoded')
        foreach ($kind in @('hidden', 'system', '.DuplicateFinder-Quarantine')) {
            $child = [IO.Directory]::CreateDirectory((Join-Path $source $kind)).FullName
            [IO.File]::WriteAllText((Join-Path $child 'excluded.png'), 'Synthetic exact image-category fixture; not decoded')
            if ($kind -eq 'hidden') { [IO.File]::SetAttributes($child, [IO.FileAttributes]'Directory, Hidden') }
            if ($kind -eq 'system') { [IO.File]::SetAttributes($child, [IO.FileAttributes]'Directory, System') }
        }
        [IO.File]::SetAttributes($source, [IO.FileAttributes]'Directory, Hidden, System')
        $device = $null
        foreach ($letter in 90..68) {
            $candidate = ([char]$letter).ToString() + ':'
            if ($null -eq [SyntheticDriveAlias]::Target($candidate)) { $device = $candidate; break }
        }
        if ($null -eq $device) { throw 'Two unused drive letters are required for this synthetic check.' }
        & "$env:SystemRoot/System32/subst.exe" $device $source
        if ($LASTEXITCODE -ne 0) { throw 'Could not create synthetic drive alias.' }
        $expected = '\??\' + $source
        $aliases += [pscustomobject]@{ Device = $device; Target = $expected; Source = $source }
        if ([SyntheticDriveAlias]::Target($device) -ne $expected) { throw 'Synthetic drive target differs from the generated fixture.' }
        if (([IO.File]::GetAttributes($device + '\') -band [IO.FileAttributes]::Hidden) -eq 0) { throw 'Synthetic drive must reproduce the hidden-root condition.' }
    }
    $testProcess = Start-Process -FilePath $executable -PassThru -WindowStyle Hidden
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $testProcess.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'DuplicateFinder'))
    Wait-Until { $script:driveWindow = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $condition); $null -ne $script:driveWindow }
    foreach ($alias in $aliases) {
        (Find-Control RootPathBox).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($alias.Device + '\')
        Invoke-Control AddFolderButton
        if ((Find-Control RootPathBox).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne '') { throw 'The add-another-folder draft was not cleared.' }
    }
    if ((Find-Control ManageFoldersButton).Current.Name -ne 'Folders (2)') { throw 'Both synthetic drives must remain selected.' }
    if ((Find-Control SelectedFoldersText).Current.Name -notlike ('*' + $aliases[0].Device + '\*')) { throw 'Selected drive is not persistently visible.' }
    $combo = Find-Control FileTypeBox
    $expand = $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 150
    $items = $combo.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))
    $image = $items | Where-Object { $_.Current.Name -eq 'Images' } | Select-Object -First 1
    if ($null -eq $image) { throw 'Missing Images category.' }
    $image.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
    Invoke-Control ScanButton
    Wait-Until { (Find-Control ScanButton).Current.IsEnabled }
    if ((Find-Control VisibleGroupsText).Current.Name -ne '1 of 1 groups') { throw 'Expected one cross-drive duplicate group.' }
    $files = (Find-Control GroupFilesList).FindAll([Windows.Automation.TreeScope]::Children,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))
    if ($files.Count -ne 2) { throw 'Hidden, system or quarantine content entered the default scan.' }
    $script:driveWindow.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize(920, 660)
    Start-Sleep -Milliseconds 200
    foreach ($id in @('SelectedFoldersText', 'ResultsList', 'GroupFilesList', 'KeepFileComboBox', 'CleanupButton')) {
        $control = Find-Control $id
        if ($control.Current.IsOffscreen -or $control.Current.BoundingRectangle.Height -lt 12) { throw "Clipped minimum-size control: $id" }
    }
    Invoke-Control ManageFoldersButton
    foreach ($name in @('Clear selection', 'Close')) {
        $button = $script:driveWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name))
        $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 150
    }
    if ((Find-Control SelectedFoldersText).Current.Name -ne 'No folders selected. Add a folder or drive.') { throw 'Cleared selections remain in the summary.' }
    Write-Output 'PASS: two synthetic hidden/system drive roots, cross-drive Images scan, preserved child exclusions, visible selection, minimum layout and clear selection. No cleanup invoked.'
}
finally {
    if ($null -ne $testProcess) {
        $testProcess.Refresh()
        if (!$testProcess.HasExited) {
            $testProcess.CloseMainWindow() | Out-Null
            if (!$testProcess.WaitForExit(3000)) { Stop-Process -Id $testProcess.Id -Force }
        }
        $testProcess.Dispose()
    }
    foreach ($alias in $aliases) {
        # Never remove another process's replacement mapping.
        if ([SyntheticDriveAlias]::Target($alias.Device) -eq $alias.Target) {
            & "$env:SystemRoot/System32/subst.exe" $alias.Device /d
            if ($LASTEXITCODE -ne 0) { Write-Warning 'Synthetic drive alias cleanup failed.' }
        }
    }
    if ([IO.Directory]::Exists($fixture)) {
        foreach ($directory in [IO.Directory]::EnumerateDirectories($fixture, '*', [IO.SearchOption]::AllDirectories)) {
            [IO.File]::SetAttributes($directory, [IO.FileAttributes]::Directory)
        }
        # Keep synthetic files for inspection; no user data was read or removed.
    }
}
