$files = @{
    "build-11" = "C:\temp\ft-build-shonll-11\RAH Non Pro.dll"
}

$targets = @("PickRandomNames", "DeobfuscateFile", "ObfuscateFile")
$asciiTargets = @("PickRandomNames", "DeobfuscateFile", "ObfuscateFile")

foreach ($name in $files.Keys) {
    $path = $files[$name]
    Write-Host "=== $name (, Unicode search) ==="
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
        if ($found) { Write-Host "  (Unicode) FOUND: $t" }
        else {
            # Try ASCII
            $asciiBytes = [System.Text.Encoding]::ASCII.GetBytes($t)
            $foundAscii = $false
            for ($i = 0; $i -lt $bytes.Length - $asciiBytes.Length; $i++) {
                $match = $true
                for ($j = 0; $j -lt $asciiBytes.Length; $j++) {
                    if ($bytes[$i+$j] -ne $asciiBytes[$j]) { $match = $false; break }
                }
                if ($match) { $foundAscii = $true; break }
            }
            if ($foundAscii) { Write-Host "  (ASCII) FOUND: $t" }
            else { Write-Host "  NOT FOUND (any encoding): $t" }
        }
    }
}
