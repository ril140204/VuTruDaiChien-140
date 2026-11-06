# Gui lenh vao game dang chay + doc live log.
#
# Cach dung (moi lenh 1 tham so -L):
#   .\game_cmd.ps1 -L "dump"
#   .\game_cmd.ps1 -L "click Tui"
#   .\game_cmd.ps1 -L "text Account tryni1"
#   .\game_cmd.ps1 -L "dump" -L "getlevel"
#   .\game_cmd.ps1 -Tail 50
#   .\game_cmd.ps1 -Reset -Tail 20
#
# Hoac truyen ca khoi lenh nhieu dong:
#   .\game_cmd.ps1 -Script "dump`nclick Tui"

param(
    [string[]]$L,
    [string]$Script,
    [int]$Tail = 30,
    [switch]$Reset,
    [switch]$All
)

$root    = "D:\game\VuTruDaiChien_140"
$log     = Join-Path $root "BepInEx\GamePlay.log"
$cmdFile = Join-Path $root "BepInEx\GameCmd.txt"

if ($Reset -and (Test-Path $log)) {
    Remove-Item $log -Force
    Write-Host "Da xoa GamePlay.log"
}

$lines = @()
if (Test-Path $log) { $lines = Get-Content $log -Encoding UTF8 }
$before = $lines.Count

# --- Thu thap lenh ---
$cmds = @()
if ($L)      { $cmds += $L }
if ($Script) { $cmds += ($Script -split "`r?`n") }
$cmds = @($cmds | Where-Object { $_.Trim().Length -gt 0 })

if ($cmds.Count -gt 0) {
    $text = $cmds -join "`r`n"
    [System.IO.File]::WriteAllText($cmdFile, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ">>> Da gui $($cmds.Count) lenh:"
    $cmds | ForEach-Object { Write-Host "    $_" }
    Start-Sleep -Milliseconds 1500
}

if (-not (Test-Path $log)) {
    Write-Host "(Chua co GamePlay.log - game chua chay?)"
    exit 0
}

$now = Get-Content $log -Encoding UTF8

if ($cmds.Count -gt 0) {
    # In phan log moi phat sinh sau khi gui lenh
    $now | Select-Object -Skip $before | ForEach-Object { $_ }
}
elseif ($All) {
    $now | ForEach-Object { $_ }
}
else {
    $now | Select-Object -Last $Tail | ForEach-Object { $_ }
}
