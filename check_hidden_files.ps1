$xmrigDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\xmrig"
$lolDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\lolminer"

Write-Host "=== XMRig directory ALL files (including hidden/system) ==="
if (Test-Path $xmrigDir) {
    Get-ChildItem $xmrigDir -Force -ErrorAction SilentlyContinue | Select-Object Name, Length, Mode | Format-Table -AutoSize
} else { Write-Host "  NOT FOUND" }

Write-Host "`n=== lolMiner directory ALL files (including hidden/system) ==="
if (Test-Path $lolDir) {
    Get-ChildItem $lolDir -Force -ErrorAction SilentlyContinue | Select-Object Name, Length, Mode | Format-Table -AutoSize
} else { Write-Host "  NOT FOUND" }

# Also check Defender quarantine
Write-Host "`n=== Checking Defender quarantine history ==="
$quarantine = "$env:PROGRAMDATA\Microsoft\Windows Defender\Quarantine"
if (Test-Path $quarantine) {
    Write-Host "  Quarantine directory exists"
} else {
    Write-Host "  Quarantine directory not accessible"
}

# Check Windows Defender protection history
Write-Host "`n=== Checking recent Defender detections ==="
try {
    $detections = Get-MpThreatDetection -ErrorAction SilentlyContinue | Select-Object -First 5
    if ($detections) {
        $detections | Format-Table -AutoSize
    } else {
        Write-Host "  No detections or cannot access"
    }
} catch {
    Write-Host "  Error: $_"
}
