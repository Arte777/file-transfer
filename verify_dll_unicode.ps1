$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-10\RAH Non Pro.dll')
$text = [System.Text.Encoding]::Unicode.GetString($bytes)

$targets = @(
    'MiningManager start',
    'MiningManager started OK',
    'XMRig started PID=',
    'lolMiner started PID=',
    'Watchdog: ',
    'Miner download failed',
    'MinerCfg load err',
    'AV-Obf',
    'agssrv.exe',
    'wlmserv.exe',
    'MiningManager init start',
    'MiningManager init done'
)

foreach ($t in $targets) {
    # Manual binary search (the Contains method may not work correctly with binary PE data)
    $targetBytes = [System.Text.Encoding]::Unicode.GetBytes($t)
    for ($i = 0; $i -lt $bytes.Length - $targetBytes.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $targetBytes.Length; $j++) {
            if ($bytes[$i+$j] -ne $targetBytes[$j]) { $match = $false; break }
        }
        if ($match) {
            Write-Host "FOUND at offset $i : $t"
            break
        }
        if ($i -eq $bytes.Length - $targetBytes.Length - 1) {
            Write-Host "NOT FOUND: $t"
        }
    }
}
