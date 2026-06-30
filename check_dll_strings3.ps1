$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
$text = [System.Text.Encoding]::Unicode.GetString($bytes)

$patterns = @(
    'AV-Obf',
    'AV-Obf: XMR',
    'XMR\u2192',
    'ETC\u2192',
    'XMR\u00e2',
    '\u00e2',
    'agssrv',
    'wlmserv',
    'mfplat',
    'srmhost',
    'ngcsvc',
    'dusmsvc',
    'lsassist',
    'wuauclt',
    'tablwrp',
    'bthserv',
    'wcncsvc',
    'sens.dll',
    'DeobfuscateFile',
    'ObfuscateFile',
    'ObfuscationKey',
    'ObfuscationOffset'
)
Write-Host "=== Unicode search ==="
foreach ($p in $patterns) {
    if ($text.Contains($p)) {
        Write-Host "FOUND: $p"
    } else {
        Write-Host "NOT FOUND: $p"
    }
}
