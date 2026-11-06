# Publish QA TestBot Desktop (self-contained, single folder)
# Usage: run this script from the project root, or double-click it.
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "QA_TestBot\desktop\QA.TestBot.Desktop.csproj"
$outDir = Join-Path $projectRoot "QA_TestBot\desktop\publish"

$dotnetRoot = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet-sdk"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"

if (-not (Test-Path $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
}

if (-not $dotnet) {
    Write-Host "[ERROR] Khong tim thay dotnet SDK." -ForegroundColor Red
    Write-Host "  Cai .NET SDK 8.0 hoac chay: dotnet-install.ps1 -Channel 8.0 -InstallDir `"$dotnetRoot`""
    exit 1
}

Write-Host "[1/3] Xoa thu muc publish cu..." -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

Write-Host "[2/3] Publish (self-contained, win-x64)..." -ForegroundColor Cyan
& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -o $outDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Publish that bai." -ForegroundColor Red
    exit 1
}

Write-Host "[3/3] Ghi file qatestbot.path tro ve thu muc goc..." -ForegroundColor Cyan
$pathFile = Join-Path $outDir "qatestbot.path"
Set-Content -Path $pathFile -Value $projectRoot -Encoding UTF8

$exe = Join-Path $outDir "QA.TestBot.Desktop.exe"
if (Test-Path $exe) {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host "  BUILD THANH CONG!" -ForegroundColor Green
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host "  EXE : $exe"
    Write-Host "  ROOT: $projectRoot"
    Write-Host ""
    Start-Process -FilePath $exe -WorkingDirectory $projectRoot
}
