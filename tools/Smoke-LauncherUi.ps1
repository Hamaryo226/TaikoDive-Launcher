[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppDirectory, [Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LauncherSmokeWindow {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$settings = Join-Path $OutputDirectory 'settings'
New-Item -ItemType Directory -Force $settings | Out-Null
# 検証用の発行フォルダーだけを使用。ゲームは実行せず、空のインストールを表示する。
$fakeGame = Join-Path $AppDirectory 'TaikoDive.exe'
if (Test-Path $fakeGame) { throw 'UI smoke test requires an isolated launcher-only publish directory.' }
Set-Content -LiteralPath $fakeGame -Value 'UI smoke fixture; do not execute' -Encoding utf8
$env:TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY = $settings
$env:TAIKODIVE_LAUNCHER_DISABLE_UPDATE_CHECK = '1'

function Select-Navigation([System.Windows.Automation.AutomationElement]$root, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    foreach ($item in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($item.Current.IsOffscreen -or -not $item.Current.IsEnabled) { continue }
        $selection = $null
        $hasSelection = $item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)
        if ($hasSelection -and $selection.Current.IsSelected) { return }
        $invoke = $null
        if ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
            try { $invoke.Invoke(); return } catch { Write-Host "Invoke pattern fallback for $name" }
        }
        if ($hasSelection) {
            try { $selection.Select(); return } catch { Write-Host "Selection pattern fallback for $name" }
        }
        if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) { continue }
        $rect = $item.Current.BoundingRectangle
        if ($rect.Width -gt 5 -and $rect.Height -gt 5) {
            [LauncherSmokeWindow]::SetCursorPos([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2)) | Out-Null
            [LauncherSmokeWindow]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
            [LauncherSmokeWindow]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
            return
        }
    }
    throw "Navigation item not found: $name"
}

function Save-Screenshot([IntPtr]$handle, [string]$path) {
    $rect = [LauncherSmokeWindow+Rect]::new()
    if (-not [LauncherSmokeWindow]::GetWindowRect($handle, [ref]$rect)) { throw 'GetWindowRect failed.' }
    $bitmap = [System.Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

try {
    foreach ($theme in @('Light', 'Dark')) {
        @{ theme = $theme; setupCompleted = $true; closeAfterLaunch = $false } | ConvertTo-Json | Set-Content (Join-Path $settings 'launcher.json') -Encoding utf8
        $process = Start-Process (Join-Path $AppDirectory 'TaikoDive.Launcher.exe') -WorkingDirectory $AppDirectory -PassThru
        try {
            for ($i = 0; $i -lt 100; $i++) {
                $process.Refresh()
                if ($process.HasExited) { throw "Launcher exited with code $($process.ExitCode)." }
                if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
                Start-Sleep -Milliseconds 200
            }
            if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'No top-level launcher window was created.' }
            $handle = $process.MainWindowHandle
            [LauncherSmokeWindow]::SetForegroundWindow($handle) | Out-Null
            Start-Sleep -Milliseconds 1000
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
            Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-start.png")
            $pages = [ordered]@{ 'ホーム' = 'home'; 'ライブラリ' = 'library'; 'プレイ記録' = 'records'; 'ツール' = 'toolkit'; 'セットアップ' = 'setup' }
            $headings = @{ home = 'TaikoDive'; library = '楽曲ライブラリ'; records = 'プレイカレンダーとリプレイ'; toolkit = 'ランチャーツール'; setup = '初回セットアップ' }
            foreach ($page in $pages.GetEnumerator()) {
                [LauncherSmokeWindow]::SetWindowPos($handle, [IntPtr]::Zero, 20, 20, 1180, 760, 0) | Out-Null
                Start-Sleep -Milliseconds 300
                Write-Host "Navigate $theme / $($page.Key)"
                Select-Navigation $root $page.Key
                Start-Sleep -Milliseconds 1500
                if ($process.HasExited) { throw "Launcher exited navigating to $($page.Key)." }
                $headingCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $headings[$page.Value])
                $heading = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $headingCondition)
                if ($null -eq $heading -or $heading.Current.IsOffscreen) { throw "Expected page heading was not visible: $($headings[$page.Value])" }
                Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-$($page.Value)-wide.png")
                [LauncherSmokeWindow]::SetWindowPos($handle, [IntPtr]::Zero, 20, 20, 720, 640, 0) | Out-Null
                Start-Sleep -Milliseconds 500
                Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-$($page.Value)-narrow.png")
            }
        }
        catch {
            if (-not $process.HasExited -and $process.MainWindowHandle -ne [IntPtr]::Zero) { Save-Screenshot $process.MainWindowHandle (Join-Path $OutputDirectory "$theme-failure.png") }
            throw
        }
        finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
    }
}
finally { Remove-Item -LiteralPath $fakeGame -Force; Remove-Item Env:TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY -ErrorAction SilentlyContinue }
