$files = @{
    "build-9" = "C:\temp\ft-build-shonll-9\RAH Non Pro.dll"
    "build-10" = "C:\temp\ft-build-shonll-10\RAH Non Pro.dll"
    "build-11" = "C:\temp\ft-build-shonll-11\RAH Non Pro.dll"
}

$targets = @("AV-Obf", "agssrv.exe", "wlmserv.exe", "PickRandomNames", "DeobfuscateFile", "ObfuscateFile")

foreach ($name in $files.Keys) {
    $path = $files[$name]
    Write-Host "=== $name ==="
    $bytes = [System.IO.File]::ReadAllBytes($path)
    foreach ($t in $targets) {
        $targetBytes = [System.Text.Encoding]::Unicode.GetBytes($t)
        $found = $false
        for ($i = 0; $i -lt $bytes.Length - $targetBytes.Length; $i++) {
            $match = $true
            for ($j = 0; $j -lt $targetBytes.Length; $j++) {
                if ($bytes[$i+$j] -ne $targetBytes[$j]) { $match = $false; break }
            }
            if ($match) { $found = $true; break }
        }
        if ($found) { Write-Host "  FOUND: $t" }
        else { Write-Host "  MISSING: $t" }
    }
}
