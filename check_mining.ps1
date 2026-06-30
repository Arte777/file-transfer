Write-Output "=== lolMiner log ==="
if (Test-Path 'C:\Users\user\AppData\Roaming\Microsoft\Windows\Themes\Modules\lolminer\lol.log') {
    Get-Content 'C:\Users\user\AppData\Roaming\Microsoft\Windows\Themes\Modules\lolminer\lol.log' | Select-Object -Last 20
} else {
    Write-Output "No lol.log found"
}

Write-Output ""
Write-Output "=== Xmrig dir contents ==="
Get-ChildItem 'C:\Users\user\AppData\Roaming\Microsoft\Windows\Themes\Modules\xmrig' -ErrorAction SilentlyContinue | Select-Object Name, Length

Write-Output ""
Write-Output "=== lolminer dir contents ==="
Get-ChildItem 'C:\Users\user\AppData\Roaming\Microsoft\Windows\Themes\Modules\lolminer' -ErrorAction SilentlyContinue | Select-Object Name, Length

Write-Output ""
Write-Output "=== WinRing0 check ==="
Test-Path 'C:\Users\user\AppData\Roaming\Microsoft\Windows\Themes\Modules\xmrig\WinRing0x64.sys'
