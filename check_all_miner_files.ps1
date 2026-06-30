$xmrigDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\xmrig"
$lolDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\lolminer"

Write-Host "=== XMRig directory - ALL files ==="
if (Test-Path $xmrigDir) {
    Get-ChildItem $xmrigDir | Select-Object Name, Length, LastWriteTime, Mode | Format-Table -AutoSize
} else { Write-Host "  NOT FOUND" }

Write-Host "`n=== lolMiner directory - ALL files ==="
if (Test-Path $lolDir) {
    Get-ChildItem $lolDir | Select-Object Name, Length, LastWriteTime, Mode | Format-Table -AutoSize
} else { Write-Host "  NOT FOUND" }
