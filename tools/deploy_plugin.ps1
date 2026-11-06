# Build plugin QLTK.Client140 va trien khai vao BepInEx\plugins.
# Tu dong tat game dang chay (neu co) truoc khi copy.
param([switch]$NoKill)

$ErrorActionPreference = "Stop"
$root = "D:\game\VuTruDaiChien_140"
$proj = Join-Path $root "tools\plugin140"
$dest = Join-Path $root "BepInEx\plugins\QLTK.Client140.dll"

$env:PATH = "C:\Users\ASUS\.dotnet;$env:PATH"
$env:DOTNET_ROOT = "C:\Users\ASUS\.dotnet"

if (-not $NoKill) {
    $procs = Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath -like "$root*" -and $_.Name -ne "QLTK.exe" }
    foreach ($p in $procs) {
        Write-Host "Tat PID $($p.ProcessId) ($($p.Name))"
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
    if ($procs) { Start-Sleep -Seconds 3 }
}

Push-Location $proj
try {
    dotnet build -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build that bai" }
} finally {
    Pop-Location
}

Copy-Item (Join-Path $proj "bin\Release\QLTK.Client140.dll") $dest -Force
Write-Host "OK -> $dest"
