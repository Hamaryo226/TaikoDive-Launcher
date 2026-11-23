[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppDirectory, [Parameter(Mandatory)][string]$OutputDirectory, [switch]$InputOnly)

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
$fixtureSettings = Join-Path $AppDirectory 'Setting.json'
$fixtureBackup = "$fixtureSettings.launcher.bak"
$activityFolder = Join-Path $AppDirectory 'Info/TaikoDiveLauncher/history'
if ((Test-Path $fakeGame) -or (Test-Path $fixtureSettings) -or (Test-Path $fixtureBackup) -or (Test-Path $activityFolder)) {
    throw 'UI smoke test requires an isolated launcher-only publish directory.'
}
Set-Content -LiteralPath $fakeGame -Value 'UI smoke fixture; do not execute' -Encoding utf8
$fixtureInput = @(0..3 | ForEach-Object {
    @{ vendorId = 65535; productId = 65535; deviceName = 'Input diagram fixture'; inputType = 'Button'; inputIndex = $_ }
})
$fixtureJson = @{
    fontOedo = 'Uninstalled Font Fixture'; fontFallback = 'Uninstalled Fallback Fixture'
    p1Keys = @{ kaLeft = @(32, 222); kaLeftControllers = @($fixtureInput[0]); donLeftControllers = @($fixtureInput[1]);
        donRightControllers = @($fixtureInput[2]); kaRightControllers = @($fixtureInput[3]) }
} | ConvertTo-Json -Depth 6
$fixtureSongs = Join-Path $AppDirectory 'Songs/LauncherSmoke'
New-Item -ItemType Directory -Force $fixtureSongs | Out-Null
$chart = Join-Path $fixtureSongs 'sample.tja'
"TITLE:検証用サンプル曲`nWAVE:sample.wav`nCOURSE:Oni`nLEVEL:3`n#START`n1000,`n#END" | Set-Content $chart -Encoding utf8
[IO.File]::WriteAllBytes((Join-Path $fixtureSongs 'sample.wav'), [byte[]]@())
$env:TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY = $settings
$env:TAIKODIVE_LAUNCHER_DISABLE_UPDATE_CHECK = '1'

New-Item -ItemType Directory -Force $activityFolder | Out-Null
$activityToday = [DateTime]::Today
$activityMonday = $activityToday.AddDays(-(([int]$activityToday.DayOfWeek + 6) % 7))
$fixturePlays = @(
    @{ Day = $activityToday; Player = '検証プレイヤー'; Song = '検証曲 A' },
    @{ Day = $activityToday; Player = '検証プレイヤー'; Song = '検証曲 B' },
    @{ Day = $activityMonday.AddDays(-1); Player = 'ゲスト記録'; Song = '検証曲 C' }
)
# Give the preview a known spread of populated days without modifying real records.
$fixturePlays += @(1..46 | ForEach-Object {
    $week = $_
    if ($week % 6 -ne 0) {
        1..(1 + $week % 5) | ForEach-Object {
            @{ Day = $activityMonday.AddDays(-7 * $week + (($week + $_) % 7)); Player = 'ゲスト記録'; Song = '検証用サンプル曲' }
        }
    }
})
for ($i = 0; $i -lt $fixturePlays.Count; $i++) {
    $entry = $fixturePlays[$i]
    @{ id = "smoke-$i"; playedAtUtc = $entry.Day.AddHours(14).ToUniversalTime().ToString('o'); playerName = $entry.Player;
       songTitle = $entry.Song; course = 3; score = 860000 } | ConvertTo-Json -Compress |
        Set-Content (Join-Path $activityFolder "smoke-$i.json") -Encoding utf8
}

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

function Find-InputControl($root, [string]$name, [System.Windows.Automation.ControlType]$type) {
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Assert-InputControl($root, [string]$name, [System.Windows.Automation.ControlType]$type) {
    $item = Find-InputControl $root $name $type
    if ($null -eq $item -or $item.Current.IsOffscreen) { throw "Expected visible input control: $name" }
    $item
}

function Select-InputOption($root, [string]$comboName, [string]$optionName) {
    $comboType = [System.Windows.Automation.ControlType]::ComboBox
    $itemType = [System.Windows.Automation.ControlType]::ListItem
    $combo = Assert-InputControl $root $comboName $comboType
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 350
    $item = Assert-InputControl $root $optionName $itemType
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 350
}

function Invoke-InputControl($item) {
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 350
}

function Assert-NoObsoleteControls($root, [string]$page) {
    $names = switch ($page) {
        'home' { '準備完了'; 'プロフィールと起動構成を整えて、ゲームを始めよう。'; '更新情報' }
        'settings' { 'オンライン'; '接続ポート'; '最後に接続したアドレス' }
        'launcher-settings' { '設定を読み直す'; '設定を退避して初期化' }
    }
    foreach ($name in $names) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
        if ($root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)) { throw "Obsolete control remains: $name" }
    }
}

function Click-ControllerRegion($item, [double]$xFraction = 0.5) {
    $rect = $item.Current.BoundingRectangle
    [LauncherSmokeWindow]::SetCursorPos([int]($rect.X + $rect.Width * $xFraction), [int]($rect.Y + $rect.Height / 2)) | Out-Null
    [LauncherSmokeWindow]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [LauncherSmokeWindow]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
}

function Test-FontPicker($root, [IntPtr]$handle, [string]$theme) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SettingsScrollViewer')
    $scroll = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $scroller = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $scroller.SetScrollPercent(-1, 100)
    Start-Sleep -Milliseconds 400
    $combo = Assert-InputControl $root '大江戸勘亭流' ([System.Windows.Automation.ControlType]::ComboBox)
    $selection = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    if ($selection.Current.GetSelection()[0].Current.Name -ne 'Uninstalled Font Fixture') { throw 'Saved font was not retained.' }
    Select-InputOption $root '大江戸勘亭流' 'Arial'
    if ($selection.Current.GetSelection()[0].Current.Name -ne 'Arial') { throw 'The installed font was not selected.' }
    Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-font-picker.png")
    Invoke-InputControl (Assert-InputControl $root '保存' ([System.Windows.Automation.ControlType]::Button))
    $saved = Get-Content -LiteralPath $fixtureSettings -Raw | ConvertFrom-Json
    if ($saved.fontOedo -ne 'Arial' -or $saved.fontFallback -ne 'Uninstalled Fallback Fixture') {
        throw 'Saving did not persist the selected font or overwrote an unchanged font.'
    }
    $scroller.SetScrollPercent(-1, 0)
    Write-Host "Validated $theme / installed font picker"
}

function Test-PlayActivity($root, [IntPtr]$handle, [string]$theme) {
    $radio = [System.Windows.Automation.ControlType]::RadioButton
    $button = [System.Windows.Automation.ControlType]::Button
    $text = [System.Windows.Automation.ControlType]::Text
    $day = $activityToday.ToString('yyyy年M月d日')
    $week = $activityMonday.ToString('yyyy年M月d日')
    $daily = Assert-InputControl $root "${day}、2回" $button
    Invoke-InputControl $daily
    $null = Assert-InputControl $root '2回' $text
    $songCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,
        "$($activityToday.ToString('M/d')) 14:00    検証曲 A    おに    860,000点    検証プレイヤー")
    if (-not $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $songCondition)) { throw 'Selecting the day did not show its plays.' }
    (Assert-InputControl $root '週別' $radio).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 450
    $null = Assert-InputControl $root "${week}からの週、2回" $button
    Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-play-activity-weekly.png")
    (Assert-InputControl $root '累計' $radio).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 450
    $null = Assert-InputControl $root "${week}からの週、累計 $($fixturePlays.Count)回" $button
    Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-play-activity-total.png")
    Select-InputOption $root 'プレイヤー' '検証プレイヤー'
    $null = Assert-InputControl $root "${week}からの週、累計 2回" $button
    Select-InputOption $root 'プレイヤー' '全プレイヤー'
    (Assert-InputControl $root '日別' $radio).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 450
    $newFile = Join-Path $activityFolder 'live.json'
    @{ id = 'smoke-live'; playedAtUtc = $activityToday.AddHours(14).ToUniversalTime().ToString('o');
       playerName = '検証プレイヤー'; songTitle = '追加の検証曲'; course = 3; score = 900000 } | ConvertTo-Json -Compress |
        Set-Content "$newFile.tmp" -Encoding utf8
    try {
        Move-Item "$newFile.tmp" $newFile
        $deadline = [DateTime]::UtcNow.AddSeconds(9)
        do {
            Start-Sleep -Milliseconds 300
            $newCell = Find-InputControl $root "${day}、3回" $button
        } until ($newCell -or [DateTime]::UtcNow -ge $deadline)
        if (-not $newCell) { throw 'The play activity did not update while the page was open.' }
    }
    finally { Remove-Item -LiteralPath $newFile, "$newFile.tmp" -Force -ErrorAction SilentlyContinue }
    Write-Host "Validated $theme / daily, weekly, cumulative plays and player filter"
}

function Test-InputSettings($root, [IntPtr]$handle, [string]$theme) {
    $button = [System.Windows.Automation.ControlType]::Button
    $radio = [System.Windows.Automation.ControlType]::RadioButton
    $text = [System.Windows.Automation.ControlType]::Text
    # D belongs to 1P and Z belongs to 2P in the isolated installation's defaults.
    $null = Assert-InputControl $root 'D 縁(左)。割り当てを変更' $button
    $null = Assert-InputControl $root 'Z 未設定。割り当てを変更' $button
    $null = Assert-InputControl $root 'Key(222) 縁(左)。割り当てを変更' $button
    $null = Assert-InputControl $root '1P' $radio
    $second = Assert-InputControl $root '2P' $radio
    foreach ($removed in @('現在の割り当て', '赤＝ドン ／ 青＝カッ。変更は保存後、次回のTaikoDive起動から反映されます。')) {
        if (Find-InputControl $root $removed $text) { throw "Redundant explanation remains: $removed" }
    }
    $second.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 350
    $null = Assert-InputControl $root 'D 未設定。割り当てを変更' $button
    $null = Assert-InputControl $root 'Z 縁(左)。割り当てを変更' $button
    if (Find-InputControl $root 'Key(222) 縁(左)。割り当てを変更' $button) { throw 'Extra key was shown for the other player.' }
    Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-input-2p.png")
    $first = Assert-InputControl $root '1P' $radio
    $first.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 350
    $null = Assert-InputControl $root 'D 縁(左)。割り当てを変更' $button
    Write-Host "Validated $theme / input player switching"

    Select-InputOption $root '操作タイプ' 'ゲームパッド'
    Select-InputOption $root 'コントローラー' 'Input diagram fixture（未接続）'
    $faceButton = Assert-InputControl $root 'B1：縁(左)。割り当てを変更' $button
    if ($faceButton.Current.AutomationId -ne 'ControllerRegion-A') { throw 'The gamepad binding is not on the colored button itself.' }
    Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-input-gamepad.png")
    $menuItem = [System.Windows.Automation.ControlType]::MenuItem
    Click-ControllerRegion $faceButton
    Invoke-InputControl (Assert-InputControl $root '✓ 縁(左)' $menuItem)

    Select-InputOption $root '操作タイプ' '太鼓型コントローラー'
    $rim = Assert-InputControl $root 'B1：縁(左)。割り当てを変更' $button
    if ($rim.Current.AutomationId -ne 'ControllerRegion-RimLeft' -or $rim.Current.BoundingRectangle.Height -lt 100) {
        throw 'The drum binding must use the colored rim, not an overlaid keycap.'
    }
    Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-input-drum.png")
    $leftFace = Assert-InputControl $root 'B2：面(左)。割り当てを変更' $button
    if ($leftFace.Current.AutomationId -ne 'ControllerRegion-FaceLeft') { throw 'The drum binding is not on the red face.' }
    Click-ControllerRegion $leftFace
    Invoke-InputControl (Assert-InputControl $root '✓ 面(左)' $menuItem)
    Click-ControllerRegion $rim 0.08
    Invoke-InputControl (Assert-InputControl $root '面(右)' $menuItem)
    $newRim = Assert-InputControl $root 'B1：面(右)。割り当てを変更' $button
    Invoke-InputControl $newRim
    Invoke-InputControl (Assert-InputControl $root '縁(左)' $menuItem)

    Select-InputOption $root '操作タイプ' 'キーボード'
    Write-Host "Validated $theme / controller diagrams"

    Invoke-InputControl (Assert-InputControl $root 'A 未設定。割り当てを変更' $button)
    foreach ($slot in @('縁(左)', '面(左)', '面(右)', '縁(右)')) {
        $null = Assert-InputControl $root $slot $menuItem
    }
    Invoke-InputControl (Assert-InputControl $root '面(右)' $menuItem)
    $key = Assert-InputControl $root 'A 面(右)。割り当てを変更' $button
    Invoke-InputControl $key
    Invoke-InputControl (Assert-InputControl $root '割り当てを解除' $menuItem)
    $null = Assert-InputControl $root 'A 未設定。割り当てを変更' $button
    Invoke-InputControl (Assert-InputControl $root 'Key(222) 縁(左)。割り当てを変更' $button)
    Invoke-InputControl (Assert-InputControl $root '面(右)' $menuItem)
    Invoke-InputControl (Assert-InputControl $root 'Key(222) 面(右)。割り当てを変更' $button)
    Invoke-InputControl (Assert-InputControl $root '割り当てを解除' $menuItem)
    if (Find-InputControl $root 'Key(222) 面(右)。割り当てを変更' $button) { throw 'Cleared extra key remained in the diagram.' }
    Write-Host "Validated $theme / assignment and clearing"

    # Leave the page clean so the rest of the smoke test isn't blocked by the unsaved-changes dialog.
    Select-Navigation $root 'ホーム'
    Start-Sleep -Milliseconds 500
    Invoke-InputControl (Assert-InputControl $root '破棄して移動' $button)
    Start-Sleep -Milliseconds 750
    Select-Navigation $root '入力設定'
    Start-Sleep -Milliseconds 750
}

try {
    foreach ($theme in @('Light', 'Dark')) {
        Set-Content -LiteralPath $fixtureSettings -Value $fixtureJson -Encoding utf8
        @{ theme = $theme; closeAfterLaunch = $false } | ConvertTo-Json | Set-Content (Join-Path $settings 'launcher.json') -Encoding utf8
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
            foreach ($removedPage in @('ライブラリ', 'ツール', 'セットアップ')) {
                $removedCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $removedPage)
                if ($null -ne $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $removedCondition)) {
                    throw "Removed navigation item is still present: $removedPage"
                }
            }
            $pages = [ordered]@{ 'ホーム' = 'home'; 'プロフィール' = 'profiles'; '楽曲' = 'songs'; 'プレイ記録' = 'play-activity'; '入力設定' = 'input'; '起動構成' = 'settings'; 'ランチャー設定' = 'launcher-settings' }
            $headings = @{ home = 'TaikoDive'; profiles = 'プロフィール'; songs = '楽曲'; 'play-activity' = 'プレイ記録'; input = '入力設定'; settings = '起動構成'; 'launcher-settings' = 'ランチャー設定' }
            foreach ($page in $pages.GetEnumerator()) {
                if ($InputOnly -and $page.Value -ne 'input') { continue }
                [LauncherSmokeWindow]::SetWindowPos($handle, [IntPtr]::Zero, 20, 20, 1180, 760, 0) | Out-Null
                Start-Sleep -Milliseconds 300
                Write-Host "Navigate $theme / $($page.Key)"
                Select-Navigation $root $page.Key
                Start-Sleep -Milliseconds 1500
                if ($process.HasExited) { throw "Launcher exited navigating to $($page.Key)." }
                $headingCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $headings[$page.Value])
                $heading = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $headingCondition)
                if ($null -eq $heading -or $heading.Current.IsOffscreen) { throw "Expected page heading was not visible: $($headings[$page.Value])" }
                Assert-NoObsoleteControls $root $page.Value
                Save-Screenshot $handle (Join-Path $OutputDirectory "$theme-$($page.Value)-wide.png")
                if ($page.Value -eq 'input') { Test-InputSettings $root $handle $theme }
                if ($page.Value -eq 'settings') { Test-FontPicker $root $handle $theme }
                if ($page.Value -eq 'play-activity') { Test-PlayActivity $root $handle $theme }
                [LauncherSmokeWindow]::SetWindowPos($handle, [IntPtr]::Zero, 20, 20, 720, 640, 0) | Out-Null
                Start-Sleep -Milliseconds 500
                if ($page.Value -eq 'input') {
                    $null = Assert-InputControl $root 'テンキー. 未設定。割り当てを変更' ([System.Windows.Automation.ControlType]::Button)
                }
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
finally {
    Remove-Item -LiteralPath $fakeGame -Force
    Remove-Item -LiteralPath $fixtureSettings, $fixtureBackup -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $activityFolder -Recurse -Force
    $fixtureRoot = [IO.Path]::GetFullPath($AppDirectory).TrimEnd('\') + '\'
    $fixtureTarget = [IO.Path]::GetFullPath($fixtureSongs)
    if (-not $fixtureTarget.StartsWith($fixtureRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup path escaped the app directory.' }
    Remove-Item -LiteralPath $fixtureTarget -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:TAIKODIVE_LAUNCHER_SETTINGS_DIRECTORY -ErrorAction SilentlyContinue
}
