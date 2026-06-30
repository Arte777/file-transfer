$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
# Search for AV-Obf as raw bytes
$target = [System.Text.Encoding]::ASCII.GetBytes('AV-Obf')
for ($i = 0; $i -lt $bytes.Length - $target.Length; $i++) {
    $match = $true
    for ($j = 0; $j -lt $target.Length; $j++) {
        if ($bytes[$i+$j] -ne $target[$j]) { $match = $false; break }
    }
    if ($match) {
        Write-Host "FOUND 'AV-Obf' as ASCII at byte offset $i"
    }
}
# Also try in Unicode (UTF-16LE)
$targetU = [System.Text.Encoding]::Unicode.GetBytes('AV-Obf')
for ($i = 0; $i -lt $bytes.Length - $targetU.Length; $i++) {
    $match = $true
    for ($j = 0; $j -lt $targetU.Length; $j++) {
        if ($bytes[$i+$j] -ne $targetU[$j]) { $match = $false; break }
    }
    if ($match) {
        Write-Host "FOUND 'AV-Obf' as Unicode at byte offset $i"
    }
}

# Also search for the system names
$sysNames = @('agssrv', 'wlmserv', 'mfplat', 'srmhost', 'ngcsvc')
foreach ($name in $sysNames) {
    $target = [System.Text.Encoding]::ASCII.GetBytes($name)
    for ($i = 0; $i -lt $bytes.Length - $target.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $target.Length; $j++) {
            if ($bytes[$i+$j] -ne $target[$j]) { $match = $false; break }
        }
        if ($match) {
            Write-Host "FOUND '$name' as ASCII at byte offset $i"
        }
    }
}
Write-Host "Search complete"
