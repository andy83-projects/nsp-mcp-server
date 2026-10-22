param(
    [Parameter(Mandatory=$true)][string]$OriginalMap,
    [Parameter(Mandatory=$true)][string]$TestDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][string]$ReportPath,
    [switch]$AllowTerminateDisposableSession
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NspSessionRestoreNative {
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
 [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out Point p);
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent,IntPtr child);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,int x,int y,int data,IntPtr extra);
 public static void Click(IntPtr dialog,IntPtr button,int x,int y,uint process) {
  var old=SetThreadDpiAwarenessContext(new IntPtr(-4));
  if(old==IntPtr.Zero) throw new InvalidOperationException("Cannot establish physical input coordinates.");
  try {
   SetForegroundWindow(dialog);
   if(!SetCursorPos(x,y)) throw new InvalidOperationException("Cannot position pointer.");
   Point p; GetPhysicalCursorPos(out p);
   if(p.X!=x || p.Y!=y) throw new InvalidOperationException("Pointer position mismatch.");
   var hit=WindowFromPoint(p);uint pid;GetWindowThreadProcessId(hit,out pid);
   if(pid!=process || (button!=IntPtr.Zero && hit!=button && !IsChild(button,hit)))
    throw new InvalidOperationException("Pointer is not over the verified discard control.");
   mouse_event(2,0,0,0,IntPtr.Zero);mouse_event(4,0,0,0,IntPtr.Zero);
  } finally { SetThreadDpiAwarenessContext(old); }
 }
}
'@
$report = [ordered]@{ success=$false; originalMap=$OriginalMap; startedUtc=[DateTime]::UtcNow.ToString('o') }
function Get-NspWindows {
    foreach($process in @(Get-Process NSP -ErrorAction SilentlyContinue)) {
        if($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $element = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            [pscustomobject]@{ Process=$process; Element=$element; Title=$element.Current.Name }
        }
    }
}
function Get-RawChildren($root) {
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $child = $walker.GetFirstChild($root)
    for($i=0; $null -ne $child -and $i -lt 128; $i++) {
        Write-Output -NoEnumerate $child
        $child = $walker.GetNextSibling($child)
    }
}
function Close-KnownDialog($dialog) {
    $handle = [IntPtr]$dialog.Current.NativeWindowHandle
    if($handle -eq [IntPtr]::Zero) { throw 'Dialog has no native window handle; refusing an ambiguous close.' }
    if(-not [NspSessionRestoreNative]::PostMessage($handle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Cannot cancel the test dialog.' }
    for($i=0; $i -lt 50; $i++) {
        if(-not [NspSessionRestoreNative]::IsWindow($handle)) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Test dialog did not close.'
}
try {
    $OriginalMap = [IO.Path]::GetFullPath($OriginalMap)
    $TestDirectory = [IO.Path]::GetFullPath($TestDirectory).TrimEnd('\') + '\'
    if($OriginalMap.IndexOf($TestDirectory, [StringComparison]::OrdinalIgnoreCase) -eq 0) { throw 'Original map must be outside the disposable test directory.' }
    $beforeHash = (Get-FileHash -LiteralPath $OriginalMap -Algorithm SHA256).Hash
    if($beforeHash -ne $ExpectedSha256) { throw 'Original-map hash does not match the expected baseline.' }
    $report.originalSha256Before = $beforeHash
    $windows = @(Get-NspWindows)
    $report.windowsBefore = @($windows | ForEach-Object { $_.Title })
    $matches = @($windows | Where-Object { $_.Title.IndexOf($TestDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if($matches.Count -gt 1) { throw 'Multiple NSP windows show the disposable test directory.' }
    if($matches.Count -eq 0) {
        $safeWindows = @($windows | Where-Object { $_.Title -eq 'Haltech NSP' -or $_.Title.IndexOf($OriginalMap, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
        if($safeWindows.Count -ne $windows.Count) { throw 'A different NSP map is open; refusing to replace it.' }
        $report.testWindowAlreadyClosed = $true
    }
    if($matches.Count -eq 1) {
    $session = $matches[0]
    $report.testWindowTitle = $session.Title
    $pendingCloseConfirmation = $false
    foreach($child in @(Get-RawChildren $session.Element)) {
        if($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
            $id = $child.Current.AutomationId
            $title = $child.Current.Name
            $report.pendingDialog = [ordered]@{ id=$id; title=$title; handle=$child.Current.NativeWindowHandle }
            if($title -match '(?i)unsaved|save changes') { $pendingCloseConfirmation = $true; continue }
            $knownAxis = $title.EndsWith(' : Table Axis Setup')
            if($id -notin @('TableAxisSetupForm','DirectValueEntryForm') -and -not $knownAxis) { throw ('Unexpected open dialog: ' + $title + ' [' + $id + ']') }
            Close-KnownDialog $child
        }
    }
    $mainHandle = [IntPtr]$session.Element.Current.NativeWindowHandle
    if(-not $pendingCloseConfirmation -and -not [NspSessionRestoreNative]::PostMessage($mainHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Cannot request a normal NSP close.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while([DateTime]::UtcNow -lt $deadline) {
        $session.Process.Refresh()
        if($session.Process.HasExited) { break }
        foreach($dialog in @(Get-RawChildren $session.Element)) {
            if($dialog.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { continue }
            $all = @($dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
            $question = (@($dialog.Current.Name) + @($all | ForEach-Object { $_.Current.Name })) -join ' '
            $noButtons = @($all | Where-Object {
                $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
                ($_.Current.Name.Replace('&','') -in @('No', "Don't Save", 'Do not save', 'Discard'))
            })
            if($question -notmatch '(?i)save' -or $noButtons.Count -ne 1) { throw ('Unrecognized close confirmation: ' + $question) }
            $report.discardPrompt = $question
            $button = $noButtons[0]
            if(-not $button.Current.IsEnabled -or $button.Current.IsOffscreen) { throw 'Discard control is not actionable.' }
            $rect = $button.Current.BoundingRectangle
            $report.discardAction = 'Verified physical click on the exact discard control; no image input'
            [NspSessionRestoreNative]::Click([IntPtr]$dialog.Current.NativeWindowHandle, [IntPtr]$button.Current.NativeWindowHandle,
                [int]($rect.Left+$rect.Width/2), [int]($rect.Top+$rect.Height/2), [uint32]$dialog.Current.ProcessId)

        }
        Start-Sleep -Milliseconds 200
    }
    $session.Process.Refresh()
    if(-not $session.Process.HasExited -and [NspSessionRestoreNative]::IsWindow($mainHandle)) {
        $currentTitle = $session.Element.Current.Name
        if(-not $AllowTerminateDisposableSession -or $currentTitle.IndexOf($TestDirectory, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw 'NSP did not close normally; no process was force-killed.'
        }
        $report.terminatedDisposableProcessId = $session.Process.Id
        $report.verifiedTitleBeforeTermination = $currentTitle
        Stop-Process -Id $session.Process.Id -Force
        $session.Process.WaitForExit(10000) | Out-Null
        if(-not $session.Process.HasExited) { throw 'Disposable NSP session did not exit.' }
        $report.testSessionClosedNormally = $false
    } else { $report.testSessionClosedNormally = $true }
    }
    $existing = @(Get-NspWindows | Where-Object { $_.Title.IndexOf($OriginalMap, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if($existing.Count -eq 0) { Start-Process -FilePath $OriginalMap }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        $restored = @(Get-NspWindows | Where-Object { $_.Title.IndexOf($OriginalMap, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    } while($restored.Count -ne 1 -and [DateTime]::UtcNow -lt $deadline)
    if($restored.Count -ne 1) { throw 'Original map did not open in exactly one NSP window.' }
    if($restored[0].Title.EndsWith('*')) { throw 'Restored original map is marked dirty.' }
    $report.restoredTitle = $restored[0].Title
    $report.originalSha256After = (Get-FileHash -LiteralPath $OriginalMap -Algorithm SHA256).Hash
    if($report.originalSha256After -ne $beforeHash) { throw 'Original-map hash changed.' }
    $report.success = $true
} catch {
    $report.error = $_.Exception.Message
} finally {
    $report.finishedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if(-not $report.success) { exit 1 }
