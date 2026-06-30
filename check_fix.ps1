$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-10\RAH Non Pro.dll')
$ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
$unicode = [System.Text.Encoding]::Unicode.GetString($bytes)

Write-Host "=== Checking launch methods in DLL ==="

# Check if ObfuscateFile method still exists (should - used for download obfuscation)
$tokens = @('DeobfuscateFile', 'ObfuscateFile', 'PickRandomNames')
foreach ($t in $tokens) {
    if ($ascii.Contains($t)) {
        Write-Host "  FOUND in ASCII: $t"
    } else {
        Write-Host "  NOT in ASCII: $t"
    }
}

# Check for the old deobfuscate comment
if ($ascii.Contains('restore original bytes')) {
    Write-Host "  WARNING: Old deobfuscation comment still present"
} else {
    Write-Host "  OK: No deobfuscation comment"
}

# Check for AV-Obf string (should be there)
if ($unicode.Contains('AV-Obf')) {
    Write-Host "  FOUND: AV-Obf"
} else {
    Write-Host "  NOT FOUND: AV-Obf"
}

# Verify DeobfuscateFile is no longer in StartXmrig/StartLolMiner context
# by checking that "DeobfuscateFile" appears only once (in method definition, not call)
$count = 0
$idx = 0
while ($idx -ge 0 -and $idx -lt $ascii.Length) {
    $idx = $ascii.IndexOf('DeobfuscateFile', $idx)
    if ($idx -ge 0) {
        $count++
        $idx++
    }
}
Write-Host "  DeobfuscateFile occurrences: $count (should be 1 - in method definition)"
