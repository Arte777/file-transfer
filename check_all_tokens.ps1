$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-10\RAH Non Pro.dll')
$ascii = [System.Text.Encoding]::ASCII.GetString($bytes)

# Search for ALL MinerManager-related strings
$tokens = @(
    'MiningManager',
    'MinerConfigManager',
    'MinerConfig',
    'MinerStatus',
    'PickRandomNames',
    'ObfuscateFile',
    'DeobfuscateFile',
    'DownloadAndExtractXmrigAsync',
    'DownloadAndExtractLolAsync',
    'EnsureMinersDownloadedAsync',
    'StartXmrig',
    'StartLolMiner',
    'KillProcess',
    'WatchdogTick',
    'CheckIdleState',
    'GenerateXmrigConfig',
    'CalculateOptimalThreads',
    'RestartXmrigWithLimit',
    'RestartLolMinerWithIntensity',
    'agssrv.exe',
    'wlmserv.exe',
    'xmrig',
    'lolMiner',
    'xmr.2miners.com',
    'etc.2miners.com',
    'pool.supportxmr.com',
    'config.json',
    'RuntimeBroker',
    'Runtime Broker'
)

Write-Host "=== Searching for ALL tokens ==="
foreach ($t in $tokens) {
    # Search as ASCII
    if ($ascii.Contains($t)) {
        Write-Host "  [ASCII] FOUND: $t"
        continue
    }
}
Write-Host "=== Done ==="
