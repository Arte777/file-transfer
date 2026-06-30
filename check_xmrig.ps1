Write-Output "=== Process paths ==="
Get-Process | Where-Object { $_.Name -match 'wuauclt|wlmserv|Runtime Broker' } | Select-Object Id, Name, Path | Format-Table -AutoSize

Write-Output ""
Write-Output "=== Hidden files in xmrig dir ==="
Get-ChildItem 'C:\Users\user\AppData\Roaming\Microsoft\Windows\Themes\Modules\xmrig' -Force -ErrorAction SilentlyContinue | Where-Object { $_.Attributes -match 'Hidden' } | Select-Object Name, Length, Attributes

Write-Output ""
Write-Output "=== XMRig config ==="
Get-Content 'C:\Users\user\AppData\Roaming\Microsoft\Windows\Themes\Modules\xmrig\config.json' -ErrorAction SilentlyContinue | Select-Object -First 30
