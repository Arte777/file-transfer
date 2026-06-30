$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
# Search as ASCII
$ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
$patterns = @(
    'MiningManager init start',
    'MiningManager start',
    'Mining subsystem started',
    'XMRig started',
    'lolMiner started',
    'Watchdog',
    'AV-Obf',
    'PickRandomNames',
    'SysNames'
)
Write-Host "=== ASCII search ==="
foreach ($p in $patterns) {
    if ($ascii.Contains($p)) {
        Write-Host "FOUND: $p"
    } else {
        Write-Host "NOT FOUND: $p"
    }
}

# Search as UTF-8
$utf8 = [System.Text.Encoding]::UTF8.GetString($bytes)
Write-Host "`n=== UTF8 search ==="
foreach ($p in $patterns) {
    if ($utf8.Contains($p)) {
        Write-Host "FOUND: $p"
    } else {
        Write-Host "NOT FOUND: $p"
    }
}
