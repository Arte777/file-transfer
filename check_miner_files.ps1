$xmrigDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\xmrig"
$lolDir = "$env:APPDATA\Microsoft\Windows\Themes\Modules\lolminer"

Write-Host "=== XMRig files ==="
if (Test-Path $xmrigDir) {
    Get-ChildItem $xmrigDir -Filter *.exe | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
    Get-ChildItem $xmrigDir -Filter *.exe | ForEach-Object {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        # Check offset 0x44 for obfuscation key pattern
        if ($bytes.Length -gt 0x4B) {
            $vals = @()
            for ($i = 0x44; $i -lt 0x4C; $i++) {
                $vals += "0x{0:X2}" -f $bytes[$i]
            }
            Write-Host ("  Bytes at 0x44: " + ($vals -join ' '))
            Write-Host ("  Expected obfuscated: A5 3B 7C D9 1E 6F 42 88")
        }
    }
} else {
    Write-Host "  Directory not found"
}

Write-Host "=== lolMiner files ==="
if (Test-Path $lolDir) {
    Get-ChildItem $lolDir -Filter *.exe | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
    Get-ChildItem $lolDir -Filter *.exe | ForEach-Object {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        if ($bytes.Length -gt 0x4B) {
            $vals = @()
            for ($i = 0x44; $i -lt 0x4C; $i++) {
                $vals += "0x{0:X2}" -f $bytes[$i]
            }
            Write-Host ("  Bytes at 0x44: " + ($vals -join ' '))
        }
    }
} else {
    Write-Host "  Directory not found"
}
