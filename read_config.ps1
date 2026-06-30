$configPath = "$env:APPDATA\Microsoft\Windows\Themes\Modules\xmrig\config.json"
if (Test-Path $configPath) {
    Write-Host "=== XMRig config.json ==="
    Get-Content $configPath
} else {
    Write-Host "config.json not found at: $configPath"
}
