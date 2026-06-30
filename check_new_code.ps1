$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-13\RAH Non Pro.dll')

# Check for strings from OLD code vs NEW code
$checks = @(
    @("OLD: ObfuscateFile called", "ObfuscateFile(path)", [System.Text.Encoding]::ASCII),
    @("OLD: Rename + obfuscate", "Rename from xmrig.exe to system-like", [System.Text.Encoding]::ASCII),
    @("NEW: ReadAllBytes(srcExe)", "ReadAllBytes(srcExe)", [System.Text.Encoding]::ASCII),
    @("NEW: Buffer.BlockCopy", "Buffer.BlockCopy(exeData", [System.Text.Encoding]::ASCII),
    @("NEW: WriteAllBytes(XmrigExe", "WriteAllBytes(XmrigExe", [System.Text.Encoding]::ASCII),
    @("NEW: XMRig extract failed", "XMRig extract failed", [System.Text.Encoding]::Unicode),
    @("OLD: XMRig obfuscated", "XMRig obfuscated", [System.Text.Encoding]::Unicode)
)

foreach ($c in $checks) {
    $desc = $c[0]
    $pattern = $c[1]
    $enc = $c[2]
    $targetBytes = $enc.GetBytes($pattern)
    $found = $false
    for ($i = 0; $i -lt $bytes.Length - $targetBytes.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $targetBytes.Length; $j++) {
            if ($bytes[$i+$j] -ne $targetBytes[$j]) { $match = $false; break }
        }
        if ($match) { $found = $true; break }
    }
    if ($found) { Write-Host "FOUND: $desc" }
    else { Write-Host "MISSING: $desc" }
}
