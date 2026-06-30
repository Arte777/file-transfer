$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
$text = [System.Text.Encoding]::ASCII.GetString($bytes)

# Check for all method/type names from MinerManager.cs
$tokens = @(
    'MiningManager',
    'MinerConfigManager',
    'MinerConfig',
    'MinerStatus',
    'InitializeAsync',
    'StartAsync',
    'Stop',
    'GetStatus',
    'UpdateConfig',
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
    'RestartLolMinerWithIntensity'
)

Write-Host "=== Method/Type names in DLL ==="
foreach ($t in $tokens) {
    if ($text.Contains($t)) {
        Write-Host "  FOUND: $t"
    } else {
        Write-Host "  NOT FOUND: $t"
    }
}
