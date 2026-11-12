param(
    [string]$OutDir = "docs\ui-v1.4",
    [string[]]$Languages = @('vi', 'en', 'fr', 'zh-Hans'),
    [string[]]$Themes = @('Light', 'Dark')
)

# Chụp ảnh giao diện v1.4 theo ma trận: giao diện sáng/tối x 4 ngôn ngữ x các trang.
# Mỗi tổ hợp mở app hai lần: (1) chưa có tệp -> màn hình trống + các trang điều hướng,
# (2) có tệp mẫu + hai quy tắc -> trang Đổi tên và hộp thoại thư viện quy tắc.
# Chụp bằng PrintWindow nên không bị cửa sổ khác che. Dữ liệu thật trong
# %LOCALAPPDATA%\BatchFileRenamer được sao lưu và khôi phục khi xong (kể cả khi lỗi).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CapMatrix {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outPath = Join-Path $repo $OutDir
New-Item -ItemType Directory -Force $outPath | Out-Null
$exe = Join-Path $repo 'src\BFR.App\bin\Release\net8.0-windows10.0.19041.0\BatchFileRenamer.exe'
if (-not (Test-Path $exe)) { throw "Chưa build Release: $exe" }

$appData = Join-Path $env:LOCALAPPDATA 'BatchFileRenamer'
$backup = Join-Path $env:TEMP ('bfr-matrix-backup-' + [guid]::NewGuid().ToString('N'))
$hadAppData = Test-Path $appData
if ($hadAppData) { Copy-Item $appData $backup -Recurse -Force }

function Restore-AppData {
    Get-Process BatchFileRenamer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    Remove-Item $appData -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadAppData) { Copy-Item $backup $appData -Recurse -Force; Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:\BFR_DEV_TIER -ErrorAction SilentlyContinue
}

function Write-Settings([string]$language, [string]$theme) {
    New-Item -ItemType Directory -Force $appData | Out-Null
    $json = @{ Language = $language; Theme = $theme; CheckForUpdates = $false; ShelfHotkeyEnabled = $false } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText((Join-Path $appData 'settings.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-AppWindow([int]$processId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $cond)
}

function Find-ByName($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Element($element) {
    try { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch { $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
}

function Save-Window([IntPtr]$handle, [string]$file) {
    $rect = New-Object CapMatrix+RECT
    [CapMatrix]::GetWindowRect($handle, [ref]$rect) | Out-Null
    [int]$w = $rect.Right - $rect.Left
    [int]$h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][CapMatrix]::PrintWindow($handle, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Start-App([string[]]$arguments) {
    Get-Process BatchFileRenamer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    $proc = if ($arguments) { Start-Process -FilePath $exe -ArgumentList $arguments -PassThru } else { Start-Process -FilePath $exe -PassThru }
    for ($i = 0; $i -lt 60 -and -not (Get-AppWindow $proc.Id); $i++) { Start-Sleep -Milliseconds 500 }
    Start-Sleep -Seconds 3
    return $proc
}

function Shoot($win, [string]$tag, [string]$name) {
    Start-Sleep -Milliseconds 700
    Save-Window ([IntPtr]$win.Current.NativeWindowHandle) (Join-Path $outPath "$tag-$name.png")
}

$demo = Join-Path $env:TEMP 'bfr-matrix-files'
$count = 0
try {
    $env:BFR_DEV_TIER = 'pro'

    foreach ($theme in $Themes) {
        foreach ($lang in $Languages) {
            $tag = "$($theme.ToLower())-$lang"
            Write-Host "== $tag" -ForegroundColor Cyan

            # ---- (1) chưa có tệp
            Write-Settings $lang $theme
            $proc = Start-App $null
            try {
                $win = Get-AppWindow $proc.Id
                if (-not $win) { throw "Không mở được cửa sổ ($tag)." }
                Shoot $win $tag '1-empty'; $count++

                $pages = [ordered]@{ 'Nav shelf' = '2-shelf'; 'Nav history' = '3-history'; 'Nav presets' = '4-presets'; 'Nav watch folder' = '5-watch'; 'Nav settings' = '6-settings' }
                foreach ($nav in $pages.Keys) {
                    $el = Find-ByName $win $nav
                    if ($el) { Invoke-Element $el; Shoot $win $tag $pages[$nav]; $count++ }
                    else { Write-Warning "Không thấy $nav ($tag)" }
                }
            }
            finally { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

            # ---- (2) có tệp mẫu
            if (Test-Path $demo) { Remove-Item $demo -Recurse -Force }
            New-Item -ItemType Directory -Force $demo | Out-Null
            $files = 'IMG_0001 copy.jpg', 'IMG_0002 copy.jpg', 'Báo cáo tháng 3 (1).docx', 'ghi chú.txt', 'Ảnh đẹp 2024.PNG'
            foreach ($f in $files) { [System.IO.File]::WriteAllText((Join-Path $demo $f), 'demo') }
            $list = Join-Path $demo '_list.txt'
            ($files | ForEach-Object { Join-Path $demo $_ }) | Set-Content -LiteralPath $list -Encoding UTF8

            Write-Settings $lang $theme
            $proc = Start-App @('--file-list', ('"' + $list + '"'))
            try {
                $win = Get-AppWindow $proc.Id
                if (-not $win) { throw "Không mở được cửa sổ có tệp ($tag)." }
                Shoot $win $tag '7-rename'; $count++

                $add = Find-ByName $win 'Add rule button'
                if ($add) {
                    Invoke-Element $add
                    Start-Sleep -Milliseconds 1000
                    Shoot $win $tag '8-rule-library'; $count++
                }
            }
            finally { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        }
    }
}
finally {
    Remove-Item $demo -Recurse -Force -ErrorAction SilentlyContinue
    Restore-AppData
}

Write-Host "Đã chụp $count ảnh tại: $outPath" -ForegroundColor Green
