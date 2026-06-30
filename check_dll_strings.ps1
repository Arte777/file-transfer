$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
$text = [System.Text.Encoding]::Unicode.GetString($bytes)
# Check for log messages from MinerManager.cs
$patterns = @(
    'MiningManager init start',
    'MiningManager start',
    'Mining subsystem started',
    'XMRig started',
    'lolMiner started',
    'Watchdog',
    'User idle',
    'AV-Obf'
)
foreach ($p in $patterns) {
    if ($text.Contains($p)) {
        Write-Host "FOUND: $p"
    } else {
        Write-Host "NOT FOUND: $p"
    }
}
