$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
$utf16le = [System.Text.Encoding]::Unicode

# Search for "AV-Obf" with .NET metadata length prefix
# Length prefix for 6 chars = 0x0C (12 bytes in UTF-16LE... actually length prefix is CHAR count not byte count)
# In #US heap, the first byte is length in bytes (not including itself), encoded.
# For 12 bytes (6 UTF-16 chars): 0x8C (0x80 | 12) OR 0x0C (for < 128)
$target = 'AV-Obf'
$targetBytes = $utf16le.GetBytes($target)
$pattern = @(0x06) + $targetBytes  # prefix = number of UTF-16 chars, not bytes

for ($i = 0; $i -lt $bytes.Length - $pattern.Length; $i++) {
    $match = $true
    for ($j = 0; $j -lt $pattern.Length; $j++) {
        if ($bytes[$i+$j] -ne $pattern[$j]) { $match = $false; break }
    }
    if ($match) {
        Write-Host "FOUND 'AV-Obf' with 0x06 prefix at offset $i"
        # Show context
        $ctx = [System.Text.Encoding]::Unicode.GetString($bytes, $i, 40)
        Write-Host "  Context: $ctx"
    }
}

# Also try with 0x0C prefix (12 bytes in UTF-16LE)
$pattern2 = @(0x0C) + $targetBytes
for ($i = 0; $i -lt $bytes.Length - $pattern2.Length; $i++) {
    $match = $true
    for ($j = 0; $j -lt $pattern2.Length; $j++) {
        if ($bytes[$i+$j] -ne $pattern2[$j]) { $match = $false; break }
    }
    if ($match) {
        Write-Host "FOUND 'AV-Obf' with 0x0C prefix at offset $i"
    }
}

# Just search for raw bytes A V - O b f without prefix
$rawPattern = $utf16le.GetBytes('AV-Obf')
for ($i = 0; $i -lt $bytes.Length - $rawPattern.Length; $i++) {
    $match = $true
    for ($j = 0; $j -lt $rawPattern.Length; $j++) {
        if ($bytes[$i+$j] -ne $rawPattern[$j]) { $match = $false; break }
    }
    if ($match) {
        Write-Host "FOUND raw 'AV-Obf' at offset $i (no prefix)"
        $ctx = [System.Text.Encoding]::Unicode.GetString($bytes, $i, 40)
        Write-Host "  Context: $ctx"
    }
}

# Now also check for the string array values
$sysNames = @('agssrv.exe', 'wlmserv.exe', 'mfplat.dll', 'srmhost.exe', 'ngcsvc.exe')
foreach ($name in $sysNames) {
    $rawPattern = $utf16le.GetBytes($name)
    for ($i = 0; $i -lt $bytes.Length - $rawPattern.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $rawPattern.Length; $j++) {
            if ($bytes[$i+$j] -ne $rawPattern[$j]) { $match = $false; break }
        }
        if ($match) {
            Write-Host "FOUND raw '$name' at offset $i"
        }
    }
}
