param(
    [string]$OutDir = "docs\ui-upgrade",
    [string]$Theme = "Light"
)

# Chụp ảnh từng trạng thái của hộp thoại nâng cấp Pro.
# Mỗi trạng thái được tạo bằng cách ghi license.json rồi mở app và bấm nút nâng cấp.
# Hộp thoại nằm TRONG cửa sổ chính (ContentDialog) nên ảnh chụp là cả cửa sổ chính.
# Dữ liệu thật trong %LOCALAPPDATA%\BatchFileRenamer được sao lưu và khôi phục khi xong.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CapUpgrade {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outPath = Join-Path $repo $OutDir
New-Item -ItemType Directory -Force $outPath | Out-Null

$appData = Join-Path $env:LOCALAPPDATA 'BatchFileRenamer'
$licenseFile = Join-Path $appData 'license.json'
$settingsFile = Join-Path $appData 'settings.json'
$exe = Join-Path $repo 'src\BFR.App\bin\Release\net8.0-windows10.0.19041.0\BatchFileRenamer.exe'

# ---- bảo vệ dữ liệu thật: sao lưu rồi khôi phục ở cuối
$backupDir = Join-Path $env:TEMP ('bfr-capture-backup-' + [guid]::NewGuid().ToString('N'))
$hadAppData = Test-Path $appData
if ($hadAppData) { Copy-Item $appData $backupDir -Recurse -Force }
Remove-Item Env:\BFR_DEV_TIER -ErrorAction SilentlyContinue   # cần đúng tier Free/Trial theo license.json

function Restore-AppData {
    Get-Process BatchFileRenamer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    Remove-Item $appData -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadAppData) { Copy-Item $backupDir $appData -Recurse -Force; Remove-Item $backupDir -Recurse -Force -ErrorAction SilentlyContinue }
}

function Write-License([hashtable]$state) {
    New-Item -ItemType Directory -Force $appData | Out-Null
    $json = $state | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText($licenseFile, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Write-Settings([string]$theme) {
    New-Item -ItemType Directory -Force $appData | Out-Null
    $json = @{ Language = 'vi'; Theme = $theme; CheckForUpdates = $false } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText($settingsFile, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Save-Window([IntPtr]$handle, [string]$file) {
    $rect = New-Object CapUpgrade+RECT
    [CapUpgrade]::GetWindowRect($handle, [ref]$rect) | Out-Null
    [int]$w = $rect.Right - $rect.Left
    [int]$h = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [CapUpgrade]::PrintWindow($handle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  -> $(Split-Path $file -Leaf) (${w}x${h})"
}

function Find-ByName($root, [string]$name, $controlType) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)))
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Start-App {
    Get-Process BatchFileRenamer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    $proc = Start-Process -FilePath $exe -PassThru
    # Khởi động lạnh có thể chậm: chờ tới khi cửa sổ xuất hiện (tối đa 30 giây).
    for ($i = 0; $i -lt 60 -and -not (Get-AppWindow $proc.Id); $i++) { Start-Sleep -Milliseconds 500 }
    Start-Sleep -Seconds 2
    return $proc
}

function Get-AppWindow([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Capture-State {
    param([string]$name, [hashtable]$license, [switch]$Context, [switch]$InfoScreen)

    Write-Host "Trạng thái: $name" -ForegroundColor Cyan
    Write-License $license
    Write-Settings $Theme

    $proc = Start-App
    try {
        $win = Get-AppWindow $proc.Id
        if (-not $win) { throw "Không mở được cửa sổ app." }

        # Chờ cửa sổ sẵn sàng (nút badge/upgrade xuất hiện và bật).
        $badge = $null
        for ($i = 0; $i -lt 30; $i++) {
            $badge = Find-ByName $win 'License tier' ([System.Windows.Automation.ControlType]::Button)
            if (-not $badge) { $badge = Find-ByName $win 'Upgrade to Pro' ([System.Windows.Automation.ControlType]::Button) }
            if ($badge -and $badge.Current.IsEnabled) { break }
            Start-Sleep -Milliseconds 500
        }

        if ($Context) {
            # Chạm vào tính năng Pro: thêm quy tắc từ thư viện thẻ cho tới khi vượt giới hạn Free (3 quy tắc).
            foreach ($type in 'FindReplace', 'Trim', 'Case', 'Insert', 'Sanitize', 'Date') {
                if (Find-ByName $win 'Upgrade to Pro dialog' ([System.Windows.Automation.ControlType]::Custom)) { break }
                $addRule = Find-ByName $win 'Add rule button' ([System.Windows.Automation.ControlType]::Button)
                if (-not $addRule) { break }
                $addRule.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 900

                $card = Find-ByName $win "Rule card $type" ([System.Windows.Automation.ControlType]::Button)
                if (-not $card) { break }
                $card.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 900
            }
        }
        else {
            if (-not $badge) { throw "Không tìm thấy nút nâng cấp/badge." }
            $badge.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }

        Start-Sleep -Seconds 2

        # Hộp thoại là nội dung trong cửa sổ chính: tìm phần tử có tên tương ứng.
        $dialog = $null
        for ($attempt = 0; $attempt -lt 20 -and -not $dialog; $attempt++) {
            foreach ($wanted in 'Upgrade to Pro dialog', 'License information') {
                $cond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $wanted)
                $dialog = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
                if ($dialog) { break }
            }

            if (-not $dialog) {
                Start-Sleep -Milliseconds 500
                if ($attempt -eq 4 -and -not $Context) {
                    # Thử bấm lại badge (lần đầu có thể chưa sẵn sàng).
                    $again = Find-ByName $win 'License tier' ([System.Windows.Automation.ControlType]::Button)
                    if (-not $again) { $again = Find-ByName $win 'Upgrade to Pro' ([System.Windows.Automation.ControlType]::Button) }
                    if ($again) { $again.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
                }
            }
        }

        if (-not $dialog) { throw "Không mở được hộp thoại nâng cấp." }
        Start-Sleep -Milliseconds 600

        # Chụp cả cửa sổ chính (hộp thoại vẽ ngay bên trong).
        Save-Window ([IntPtr]$win.Current.NativeWindowHandle) (Join-Path $outPath "$name.png")
    }
    finally {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
}

try {
$now = (Get-Date).ToUniversalTime()

# 1) Chưa dùng thử: có cả "Mua Pro" và "Dùng thử Pro 14 ngày"
Capture-State -Name 'upgrade-1-not-trialed' -license @{}

# 2) Đang dùng thử: ẩn nút dùng thử, hiện số ngày còn lại
Capture-State -Name 'upgrade-2-trialing' -license @{
    TrialStartedUtc = $now.AddDays(-1).ToString('o')
    TrialProductId   = 'BFR_PRO_LIFETIME'
}

# 3) Hết hạn dùng thử: chỉ còn "Mua Pro" + nhập mã kích hoạt
Capture-State -Name 'upgrade-3-trial-expired' -license @{
    TrialStartedUtc = $now.AddDays(-20).ToString('o')
    TrialProductId   = 'BFR_PRO_LIFETIME'
}

# 4) Đã là Pro: badge "Pro" + màn hình thông tin license
Capture-State -Name 'upgrade-4-license-info' -license @{
    PurchasedProductId = 'BFR_PRO_LIFETIME'
    PurchasedUtc      = $now.AddDays(-30).ToString('o')
    ActivationSource  = 'store'
} -InfoScreen

# 5) Ngữ cảnh: chạm tính năng Pro khi đang ở cấp Free
Capture-State -Name 'upgrade-5-context' -license @{} -Context

}
finally { Restore-AppData }
Write-Host "Đã lưu ảnh tại: $outPath" -ForegroundColor Green
