# ==============================================================================
# Script đóng gói MSIX và Portable ZIP cho Batch File Renamer (Phase 2)
# ==============================================================================

param (
    [string]$Configuration = "Release",
    [string]$OutputDir = "artifacts",
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$artifactsPath = Join-Path $repoRoot $OutputDir
$layoutDir = Join-Path $artifactsPath "msix-layout"
$version = "2.0.0"
$msixOutput = Join-Path $artifactsPath "BatchFileRenamer-$version.msix"
$zipOutput = Join-Path $artifactsPath "BatchFileRenamer-$version-portable-win-x64.zip"

Write-Host ">>> Dọn dẹp thư mục đầu ra: $artifactsPath" -ForegroundColor Cyan
if (Test-Path $layoutDir) { Remove-Item $layoutDir -Recurse -Force }
New-Item -ItemType Directory -Path $layoutDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $layoutDir "Assets") -Force | Out-Null

Write-Host ">>> Xuất bản (Publish) BFR.App..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\BFR.App\BFR.App.csproj" -c $Configuration -r win-x64 --self-contained true -o $layoutDir

Write-Host ">>> Xuất bản (Publish) BFR.Shell (COM local server)..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\BFR.Shell\BFR.Shell.csproj" -c $Configuration -r win-x64 --self-contained true -o $layoutDir

Write-Host ">>> Xuất bản (Publish) BFR.Cli..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\BFR.Cli\BFR.Cli.csproj" -c $Configuration -r win-x64 --self-contained true -o $layoutDir

Write-Host ">>> Sao chép AppxManifest.xml và Assets..." -ForegroundColor Cyan
Copy-Item "$repoRoot\packaging\msix\AppxManifest.xml" -Destination "$layoutDir\AppxManifest.xml" -Force
Copy-Item "$repoRoot\packaging\msix\Assets\*" -Destination "$layoutDir\Assets\" -Recurse -Force

# Không đưa PDB vào gói phát hành (giảm kích thước và tránh lộ đường dẫn build).
Get-ChildItem -Path $layoutDir -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# Kiểm tra các thành phần bắt buộc trước khi đóng gói.
$required = @(
    "AppxManifest.xml",
    "BatchFileRenamer.exe",
    "bfr.exe",
    "BFR.Shell.exe",
    "BFR.Shell.dll",
    "BFR.Shell.runtimeconfig.json",
    "BFR.Shell.deps.json",
    "BFR.Store.dll",
    "Assets\StoreLogo.png",
    "Assets\Square44x44Logo.png",
    "Assets\Square150x150Logo.png",
    "Assets\Wide310x150Logo.png",
    "Assets\SplashScreen.png"
)
foreach ($item in $required) {
    if (-not (Test-Path (Join-Path $layoutDir $item))) {
        throw "Thiếu thành phần bắt buộc trong MSIX layout: $item"
    }
}

# Tạo bản Portable ZIP
Write-Host ">>> Đóng gói Portable ZIP: $zipOutput" -ForegroundColor Cyan
if (Test-Path $zipOutput) { Remove-Item $zipOutput -Force }
Compress-Archive -Path "$layoutDir\*" -DestinationPath $zipOutput -Force
Write-Host "    Portable ZIP sẵn sàng: $zipOutput" -ForegroundColor Green

# Tìm MakeAppx.exe trong Windows SDK
Write-Host ">>> Tìm kiếm MakeAppx.exe..." -ForegroundColor Cyan
$sdkRoots = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
    "${env:ProgramFiles}\Windows Kits\10\bin"
)
$makeAppx = $null
foreach ($root in $sdkRoots) {
    if (Test-Path $root) {
        $found = Get-ChildItem -Path $root -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*x64*" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) {
            $makeAppx = $found.FullName
            break
        }
    }
}

# Fallback cho máy dev không cài Windows SDK: dùng Microsoft.Windows.SDK.BuildTools từ NuGet.
if (-not $makeAppx) {
    $nugetBuildTools = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"
    if (Test-Path $nugetBuildTools) {
        $found = Get-ChildItem -Path $nugetBuildTools -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\x64\*" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { $makeAppx = $found.FullName }
    }
}

if ($makeAppx -and (Test-Path $makeAppx)) {
    Write-Host "    Tìm thấy MakeAppx: $makeAppx" -ForegroundColor Green
    Write-Host ">>> Đóng gói MSIX: $msixOutput" -ForegroundColor Cyan

    # Không dùng /nv: để MakeAppx kiểm tra schema AppxManifest và cấu trúc gói.
    & $makeAppx pack /d $layoutDir /p $msixOutput /o
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    Gói MSIX tạo thành công: $msixOutput" -ForegroundColor Green

        if (-not $SkipSign) {
            $signTool = $null
            foreach ($root in $sdkRoots) {
                if (-not (Test-Path $root)) { continue }
                $found = Get-ChildItem -Path $root -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -like "*x64*" } |
                    Sort-Object FullName -Descending |
                    Select-Object -First 1
                if ($found) { $signTool = $found.FullName; break }
            }
            if (-not $signTool) {
                $nugetBuildTools = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"
                if (Test-Path $nugetBuildTools) {
                    $found = Get-ChildItem -Path $nugetBuildTools -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
                        Where-Object { $_.FullName -like "*\x64\*" } |
                        Sort-Object FullName -Descending |
                        Select-Object -First 1
                    if ($found) { $signTool = $found.FullName }
                }
            }

            $pfxPath = $env:BFR_PFX_PATH
            $pfxPassword = $env:BFR_PFX_PASSWORD
            if ($signTool -and $pfxPath -and (Test-Path $pfxPath)) {
                Write-Host ">>> Ký thử gói MSIX..." -ForegroundColor Cyan
                & $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword $msixOutput
                if ($LASTEXITCODE -ne 0) {
                    Write-Warning "Ký gói thất bại (Exit code $LASTEXITCODE). Gói vẫn có thể dùng để kiểm tra cục bộ."
                }
            } else {
                Write-Host "    Bỏ qua ký: đặt BFR_PFX_PATH/BFR_PFX_PASSWORD để bật ký thử." -ForegroundColor Yellow
            }
        }
    } else {
        throw "MakeAppx thất bại (Exit code $LASTEXITCODE). Kiểm tra AppxManifest.xml và các thành phần trong layout."
    }
} else {
    Write-Warning "Không tìm thấy MakeAppx.exe trên máy hiện tại. Bố cục MSIX sẵn sàng tại: $layoutDir"
    Write-Warning "Trên CI windows-latest, Windows SDK có sẵn và bước MakeAppx sẽ chạy."
}

Write-Host ">>> Hoàn tất quy trình đóng gói!" -ForegroundColor Green
