param(
    [string]$WorkDir = "$env:TEMP\bfr-ui-smoke"
)

# Smoke test tự động cho luồng chính của giao diện v1.4 (thanh điều hướng trái, hộp thoại trong cửa sổ):
# Thêm tệp → Xem trước → Áp dụng (có xác nhận) → Hoàn tác, Command Palette, Regex tester, thư viện quy tắc,
# đổi thứ tự quy tắc và các trang Khay tệp / Lịch sử / Preset / Theo dõi / Cài đặt.
# Dùng UI Automation (không cần FlaUI/WinAppDriver).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationTypes

function Get-AppWindow([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Find-Descendant($parent, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-Button($parent, [string]$name) {
    $byName = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName)
}

function Find-Edit($parent, [string]$name) {
    $byName = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Edit)))
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName)
}

function Find-Dialog($parent, [string]$name) {
    $byName = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)))
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName)
}

# Chờ một điều kiện trở thành đúng (chống race khi UI cập nhật bất đồng bộ).
function Wait-For([scriptblock]$condition, [int]$timeoutMs = 6000) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        try { if (& $condition) { return $true } } catch { }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

# Đếm số phần tử Edit bên trong một phần tử.
function Count-Edits($parent) {
    return $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Edit))).Count
}

function Invoke-Element($element) {
    try {
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    }
    catch {
        # Một số control không hỗ trợ Invoke: bấm chuột theo toạ độ.
        $rect = $element.Current.BoundingRectangle
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
            [int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
        Add-Type -MemberDefinition @"
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
"@ -Name Mouse -Namespace Bfr
        [Bfr.Mouse]::mouse_event(0x0002, 0, 0, 0, [System.IntPtr]::Zero)
        [Bfr.Mouse]::mouse_event(0x0004, 0, 0, 0, [System.IntPtr]::Zero)
    }
}

# ---------------------------------------------------------------- dữ liệu thử
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
$names = @('Anh 01.jpg', 'Anh 02.jpg', 'Anh 10.jpg')
foreach ($n in $names) { [System.IO.File]::WriteAllText((Join-Path $WorkDir $n), 'demo') }
$list = Join-Path $WorkDir 'list.txt'
$names | ForEach-Object { Join-Path $WorkDir $_ } | Set-Content -LiteralPath $list -Encoding UTF8

# ---------------------------------------------------------------- bảo vệ dữ liệu thật của người dùng
# App ghi lịch sử/nhật ký vào %LOCALAPPDATA%\BatchFileRenamer (dùng chung cả khi chạy MSIX). Sao lưu rồi khôi phục;
# chạy bằng tier Pro để việc đổi tên thử không cắt bớt lịch sử của gói Free.
$appData = Join-Path $env:LOCALAPPDATA 'BatchFileRenamer'
$appBackup = Join-Path $env:TEMP ('bfr-smoke-backup-' + [guid]::NewGuid().ToString('N'))
$hadAppData = Test-Path $appData
if ($hadAppData) { Copy-Item $appData $appBackup -Recurse -Force }
New-Item -ItemType Directory -Force $appData | Out-Null
[System.IO.File]::WriteAllText((Join-Path $appData 'settings.json'),
    '{"Language":"vi","Theme":"Light","CheckForUpdates":false,"ShelfHotkeyEnabled":false}',
    (New-Object System.Text.UTF8Encoding($false)))
$env:BFR_DEV_TIER = 'pro'

# ---------------------------------------------------------------- chạy app
Get-Process BatchFileRenamer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$exe = (Resolve-Path 'src\BFR.App\bin\Release\net8.0-windows10.0.19041.0\BatchFileRenamer.exe').Path
$proc = Start-Process -FilePath $exe -ArgumentList ('--file-list "' + $list + '" --preset "Đánh số theo thứ tự"') -PassThru
# Khởi động lạnh có thể chậm: chờ tới khi cửa sổ xuất hiện (tối đa 30 giây).
for ($i = 0; $i -lt 60 -and -not (Get-AppWindow $proc.Id); $i++) { Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 3

$failures = New-Object System.Collections.Generic.List[string]
try {
    $win = Get-AppWindow $proc.Id
    if ($null -eq $win) { throw "Không tìm thấy cửa sổ ứng dụng." }
    Write-Output "OK  mở cửa sổ: $($win.Current.Name)"

    # 1) Bảng xem trước có dòng dữ liệu
    $grid = Find-Descendant $win 'Preview table'
    if ($null -eq $grid) { $failures.Add("Không tìm thấy bảng xem trước.") }
    else {
        $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::DataItem)))
        Write-Output "OK  bảng xem trước có $($rows.Count) dòng"
        if ($rows.Count -lt 3) { $failures.Add("Bảng xem trước thiếu dòng (có $($rows.Count)).") }
    }

    # 2) Mở tuỳ chọn của thẻ quy tắc (menu ⋯ → Mở tuỳ chọn): form tham số hiện tại chỗ
    $editsBefore = Count-Edits $win
    $actions = Find-Button $win 'Rule actions'
    if ($null -eq $actions) { $failures.Add("Không tìm thấy nút menu thao tác quy tắc.") }
    else {
        Invoke-Element $actions
        $script:expand = $null
        [void](Wait-For { $script:expand = Find-Descendant ([System.Windows.Automation.AutomationElement]::RootElement) 'Rule menu expand'; $null -ne $script:expand } 3000)
        if ($null -eq $script:expand) { $failures.Add("Menu quy tắc thiếu mục mở tuỳ chọn.") }
        else {
            Invoke-Element $script:expand
            $opened = Wait-For { (Count-Edits $win) -gt $editsBefore }
            $editsAfter = Count-Edits $win
            if (-not $opened) { $failures.Add("Mở tuỳ chọn quy tắc không hiện form tham số (ô nhập: $editsBefore -> $editsAfter).") }
            else { Write-Output "OK  mở rộng thẻ quy tắc tại chỗ (ô nhập: $editsBefore -> $editsAfter)" }
        }
    }

    # 3) Lọc + tìm kiếm
    $filter = Find-Descendant $win 'Filter will change'
    if ($null -eq $filter) { $failures.Add("Không tìm thấy bộ lọc trạng thái.") }
    else { Write-Output "OK  có bộ lọc trạng thái" }

    # 4) Áp dụng → hộp thoại xác nhận → xác nhận
    $apply = $null
    $buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))
    foreach ($b in $buttons) {
        if ($b.Current.Name -eq 'Apply renaming') { $apply = $b; break }
    }
    if ($null -eq $apply) { $failures.Add("Không tìm thấy nút Áp dụng.") }
    else {
        Invoke-Element $apply
        Start-Sleep -Seconds 2

        # Hộp thoại xác nhận nằm trong cửa sổ chính (ContentDialog): nút chính tên "Áp dụng".
        $script:applyBtn = $null
        [void](Wait-For { $script:applyBtn = Find-Button $win 'Áp dụng'; $null -ne $script:applyBtn } 4000)
        if ($null -eq $script:applyBtn) { $failures.Add("Không mở hộp thoại xác nhận áp dụng (thiếu nút Áp dụng).") }
        else {
            Write-Output "OK  hộp thoại xác nhận áp dụng"
            Invoke-Element $script:applyBtn
        }
        Start-Sleep -Seconds 4

        $renamed = Get-ChildItem $WorkDir -File | Where-Object { $_.Name -match '^\d{3}\.jpg$' }
        if ($renamed.Count -ne 3) { $failures.Add("Sau khi áp dụng cần 3 tệp được đánh số, thấy $($renamed.Count).") }
        else { Write-Output "OK  đã đổi tên 3 tệp: $(($renamed.Name -join ', '))" }
    }

    # 5) Hoàn tác
    $undo = Find-Descendant $win 'Undo last rename'
    if ($null -eq $undo) { $failures.Add("Không tìm thấy nút Hoàn tác.") }
    else {
        Invoke-Element $undo
        Start-Sleep -Seconds 4
        $restored = Get-ChildItem $WorkDir -File | Where-Object { $_.Name -in $names }
        if ($restored.Count -ne 3) { $failures.Add("Sau khi hoàn tác cần 3 tệp về tên cũ, thấy $($restored.Count).") }
        else { Write-Output "OK  hoàn tác thành công" }
    }

    # 6) Command Palette (Ctrl+K) — A4
    Add-Type -AssemblyName Microsoft.VisualBasic
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class BringToFront {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
    $palette = $null
    for ($attempt = 0; $attempt -lt 6 -and $null -eq $palette; $attempt++) {
        [Microsoft.VisualBasic.Interaction]::AppActivate($proc.Id) | Out-Null
        [BringToFront]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
        try { $win.SetFocus() } catch { }
        Start-Sleep -Milliseconds 800
        [System.Windows.Forms.SendKeys]::SendWait('^k')
        Start-Sleep -Seconds 2
        $palette = Find-Dialog $win 'Command palette dialog'
    }
    if ($null -eq $palette) { $failures.Add("Ctrl+K không mở được Command Palette.") }
    else {
        Write-Output "OK  mở Command Palette bằng Ctrl+K"

        $search = Find-Edit $palette 'Command palette search'
        if ($null -eq $search) { $failures.Add("Command Palette thiếu ô tìm kiếm.") }
        else {
            $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('lich')
            Start-Sleep -Milliseconds 900
            $items = $palette.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem)))
            if ($items.Count -lt 1) { $failures.Add("Lọc 'lich' không ra lệnh nào.") }
            else { Write-Output "OK  lọc lệnh không dấu ('lich'): $($items.Count) kết quả" }
        }

        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 800
    }

    # 7) Trình thử Regex (A1)
    $addRule = Find-Button $win 'Add rule button'
    if ($null -eq $addRule) { $failures.Add("Không tìm thấy nút thêm quy tắc.") }
    else {
        Invoke-Element $addRule
        Start-Sleep -Milliseconds 900

        # Thư viện quy tắc (hộp thoại trong cửa sổ): bấm thẻ "Tìm và thay".
        $card = Find-Button $win 'Rule card FindReplace'
        $chosen = $false
        if ($null -ne $card) { Invoke-Element $card; $chosen = $true }
        Start-Sleep -Milliseconds 1200

        if (-not $chosen) { $failures.Add("Thư viện quy tắc không có thẻ Tìm và thay.") }
        else {
            Write-Output "OK  thêm quy tắc từ thư viện thẻ"
            $regexBtn = Find-Button $win 'Open regex tester'
            if ($null -eq $regexBtn) { $failures.Add("Không tìm thấy nút Trình thử Regex trên thẻ quy tắc.") }
            else {
                Invoke-Element $regexBtn
                Start-Sleep -Seconds 2
                $regex = Find-Dialog $win 'Regex tester dialog'
                if ($null -eq $regex) { $failures.Add("Không mở được Trình thử Regex.") }
                else {
                    Write-Output "OK  mở Trình thử Regex"

                    $pattern = Find-Edit $regex 'Regex pattern'
                    $replacement = Find-Edit $regex 'Regex replacement'
                    $input = Find-Edit $regex 'Regex test string'
                    if ($null -eq $pattern -or $null -eq $input) { $failures.Add("Trình thử Regex thiếu ô nhập.") }
                    else {
                        $pattern.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('^(\d+)_(\w+)$')
                        Start-Sleep -Milliseconds 500
                        if ($null -ne $replacement) {
                            $replacement.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('$2_$1')
                            Start-Sleep -Milliseconds 400
                        }
                        $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('2024_file')

                        # Chờ kết quả xuất hiện trong ô kết quả (ô TextBox read-only).
                        $hasResult = Wait-For {
                            $edits = $regex.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                                (New-Object System.Windows.Automation.PropertyCondition(
                                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                                    [System.Windows.Automation.ControlType]::Edit)))
                            foreach ($e in $edits) {
                                try {
                                    $value = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
                                    if ($value -like '*file_2024*') { return $true }
                                }
                                catch { }
                            }
                            return $false
                        }

                        if (-not $hasResult) { $failures.Add("Trình thử Regex không ra kết quả 'file_2024'.") }
                        else { Write-Output "OK  Trình thử Regex tính đúng kết quả thay thế" }
                    }

                    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                    Start-Sleep -Milliseconds 800
                }
            }
        }
    }

    # 8) Đổi thứ tự quy tắc: menu ⋯ → Đưa xuống
    $actions = Find-Button $win 'Rule actions'
    if ($null -eq $actions) { $failures.Add("Không tìm thấy menu thao tác quy tắc.") }
    else {
        Invoke-Element $actions
        $script:moveDown = $null
        [void](Wait-For { $script:moveDown = Find-Descendant ([System.Windows.Automation.AutomationElement]::RootElement) 'Rule menu move down'; $null -ne $script:moveDown } 3000)
        if ($null -eq $script:moveDown) { $failures.Add("Menu quy tắc thiếu mục đưa xuống.") }
        else {
            Invoke-Element $script:moveDown
            Start-Sleep -Milliseconds 1000
            Write-Output "OK  đổi thứ tự quy tắc bằng menu"
        }
    }

    # 9) Các trang của thanh điều hướng trái: mỗi chức năng một trang
    function Select-Nav([string]$name) {
        $nav = Find-Descendant $win $name
        if ($null -eq $nav) { return $false }
        try { $nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
        catch { Invoke-Element $nav }
        Start-Sleep -Milliseconds 900
        return $true
    }

    foreach ($page in 'Nav shelf', 'Nav history', 'Nav presets', 'Nav watch folder') {
        if (-not (Select-Nav $page)) { $failures.Add("Không tìm thấy mục điều hướng '$page'.") }
        elseif ($proc.HasExited) { $failures.Add("App thoát khi mở trang '$page'.") }
        else { Write-Output "OK  mở trang '$page'" }
    }

    if (-not (Select-Nav 'Nav settings')) { $failures.Add("Không tìm thấy mục điều hướng cài đặt.") }
    else {
        $settings = Find-Descendant $win 'Settings window'
        if ($null -eq $settings) { $failures.Add("Không mở được trang cài đặt.") }
        else {
            Write-Output "OK  mở trang cài đặt"
            $checkNow = Find-Button $settings 'Check for updates now'
            if ($null -eq $checkNow) { $failures.Add("Trang cài đặt thiếu nút 'Kiểm tra ngay'.") }
            else { Write-Output "OK  trang cài đặt có nút kiểm tra cập nhật" }
        }
    }
    [void](Select-Nav 'Nav rename')
}
finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    # Khôi phục dữ liệu người dùng như trước khi chạy.
    Remove-Item $appData -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadAppData) { Copy-Item $appBackup $appData -Recurse -Force; Remove-Item $appBackup -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:\BFR_DEV_TIER -ErrorAction SilentlyContinue
}

Write-Output ''
if ($failures.Count -eq 0) {
    Write-Output 'SMOKE UI: PASS'
    exit 0
}

Write-Output "SMOKE UI: FAIL ($($failures.Count))"
$failures | ForEach-Object { Write-Output "  - $_" }
exit 1
