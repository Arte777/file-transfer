# Clean everything
$appData = "$env:APPDATA\Microsoft\Windows\Themes"

# Kill our clones (not system RuntimeBroker)
Get-CimInstance Win32_Process -Filter "Name like '%Runtime Broker%' AND not ExecutablePath like '%System32%'" | ForEach-Object {
    Write-Host ("Killing " + $_.ProcessId + " " + $_.ExecutablePath)
    $_.Terminate()
}

# Clean miner modules
$modulesDir = "$appData\Modules"
if (Test-Path $modulesDir) {
    Remove-Item -Path $modulesDir -Recurse -Force
    Write-Host "Deleted Modules directory"
}

# Clean RuntimeBroker directory
$brokerDir = "$appData\RuntimeBroker"
if (Test-Path $brokerDir) {
    Remove-Item -Path $brokerDir -Recurse -Force
    Write-Host "Deleted RuntimeBroker directory"
}

# Delete poolcfg.json
$poolCfg = "$appData\Modules\poolcfg.json"
if (Test-Path $poolCfg) {
    Remove-Item -Path $poolCfg -Force
    Write-Host "Deleted poolcfg.json"
}

# Clear log
$logFile = "$appData\ft.log"
if (Test-Path $logFile) {
    Clear-Content $logFile -Force
    Write-Host "Cleared log"
}

Write-Host "Cleanup complete"
