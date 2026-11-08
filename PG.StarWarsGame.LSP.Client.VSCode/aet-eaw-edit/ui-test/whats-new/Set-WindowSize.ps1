<#
.SYNOPSIS
    Sizes a VS Code window from outside, since the driver cannot: Chromedriver's window commands are not
    implemented for Electron ("Browser.getWindowForTarget wasn't found").

.DESCRIPTION
    Finds the first Code process whose main window title contains TitlePart, restores it if maximized,
    and sets its outer size. The caller reads the page's own size afterwards and corrects the difference.
#>
param(
    [Parameter(Mandatory)] [string] $TitlePart,
    [Parameter(Mandatory)] [int] $Width,
    [Parameter(Mandatory)] [int] $Height
)

$ErrorActionPreference = 'Stop'

Add-Type -Namespace AetUi -Name Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'@

$window = Get-Process Code | Where-Object { $_.MainWindowTitle -like "*$TitlePart*" } | Select-Object -First 1
if (-not $window) { throw "No VS Code window whose title contains '$TitlePart'" }

$SW_RESTORE = 9
$SWP_NOZORDER = 0x0004
$SWP_SHOWWINDOW = 0x0040
[void][AetUi.Win32]::ShowWindow($window.MainWindowHandle, $SW_RESTORE)
if (-not [AetUi.Win32]::SetWindowPos($window.MainWindowHandle, [IntPtr]::Zero, 0, 0, $Width, $Height, $SWP_NOZORDER -bor $SWP_SHOWWINDOW)) {
    throw "SetWindowPos failed for process $($window.Id)"
}
Write-Output "$($window.Id) $($window.MainWindowTitle)"
