$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
# Search for "AV-Obf: XMR" in various encodings
$encodings = @(
    @{Name="ASCII"; Encoding=[System.Text.Encoding]::ASCII},
    @{Name="UTF8"; Encoding=[System.Text.Encoding]::UTF8},
    @{Name="Unicode"; Encoding=[System.Text.Encoding]::Unicode},
    @{Name="BigEndianUnicode"; Encoding=[System.Text.Encoding]::BigEndianUnicode},
    @{Name="UTF32"; Encoding=[System.Text.Encoding]::UTF32}
)

$allPatterns = @(
    'AV-Obf',
    'AV-Obf: ',
    'AV-Obf: XMR',
    'AV-Obf: XMR\u2192',
    'XMR\u2192{0}',
    '{0} ETC\u2192{1}',
    'PickRandomNames',
    'MiningManager start',
    'XMRig started PID=',
    'lolMiner started PID=',
    'agssrv.exe',
    'wlmserv.exe',
    'mfplat.dll',
    'srmhost.exe',
    'ngcsvc.exe',
    'dusmsvc.exe',
    'lsassist.exe',
    'wuauclt.exe',
    'tablwrp.exe',
    'bthserv.exe',
    'wcncsvc.exe',
    'sens.dll',
    'Watchdog: XMRig dead',
    'Watchdog: lolMiner dead',
    'User idle detected',
    'User active',
    'DeobfuscateFile',
    'ObfuscateFile'
)

Write-Host "=== Searching in all encodings ==="
foreach ($enc in $encodings) {
    $text = $enc.Encoding.GetString($bytes)
    foreach ($p in $allPatterns) {
        if ($text.Contains($p)) {
            Write-Host "  [$($enc.Name)] FOUND: $p"
        }
    }
}
Write-Host "=== Done ==="
