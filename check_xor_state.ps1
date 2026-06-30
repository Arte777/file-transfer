$xmrigDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\xmrig"
$lolDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\lolminer"

# Get ALL files with ALL properties
Write-Host "=== XMRig full file info ==="
if (Test-Path $xmrigDir) {
    Get-ChildItem $xmrigDir -Force | ForEach-Object {
        $attrs = $_.Attributes.ToString()
        $len = $_.Length
        $name = $_.Name
        $time = $_.LastWriteTime
        Write-Host ("  {0,-25} {1,10} {2,-30} {3}" -f $name, $len, $attrs, $time)
    }
}

Write-Host "`n=== lolMiner full file info ==="
if (Test-Path $lolDir) {
    Get-ChildItem $lolDir -Force | ForEach-Object {
        $attrs = $_.Attributes.ToString()
        $len = $_.Length
        $name = $_.Name
        $time = $_.LastWriteTime
        Write-Host ("  {0,-25} {1,10} {2,-30} {3}" -f $name, $len, $attrs, $time)
    }
}

# Check XOR bytes of xmrig.exe
Write-Host "`n=== XOR status check ==="
$xmrigExe = Join-Path $xmrigDir "xmrig.exe"
if (Test-Path $xmrigExe) {
    $bytes = [System.IO.File]::ReadAllBytes($xmrigExe)
    if ($bytes.Length -gt 0x4B) {
        $vals = for ($i = 0x44; $i -lt 0x4C; $i++) { "0x{0:X2}" -f $bytes[$i] }
        Write-Host ("  xmrig.exe bytes at 0x44: " + ($vals -join ' '))
        Write-Host ("  Expected if obfuscated: 0xA5 0x3B 0x7C 0xD9 0x1E 0x6F 0x42 0x88")
    }
}

$lolExe = Join-Path $lolDir "lolMiner.exe"
if (Test-Path $lolExe) {
    $bytes = [System.IO.File]::ReadAllBytes($lolExe)
    if ($bytes.Length -gt 0x4B) {
        $vals = for ($i = 0x44; $i -lt 0x4C; $i++) { "0x{0:X2}" -f $bytes[$i] }
        Write-Host ("  lolMiner.exe bytes at 0x44: " + ($vals -join ' '))
    }
}
