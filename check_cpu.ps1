$p1 = (Get-Process -Id 26616 -ErrorAction SilentlyContinue).CPU
$p2 = (Get-Process -Id 12180 -ErrorAction SilentlyContinue).CPU
Start-Sleep 5
$p3 = (Get-Process -Id 26616 -ErrorAction SilentlyContinue).CPU
$p4 = (Get-Process -Id 12180 -ErrorAction SilentlyContinue).CPU
Write-Output "XMRig CPU in 5s: $([math]::Round($p3 - $p1, 2))s (of 5s wall)"
Write-Output "lolMiner CPU in 5s: $([math]::Round($p4 - $p2, 2))s (of 5s wall)"
Write-Output ""
Write-Output "XMRig Mem: $([math]::Round((Get-Process -Id 26616 -ErrorAction SilentlyContinue).WorkingSet64/1MB))MB"
Write-Output "lolMiner Mem: $([math]::Round((Get-Process -Id 12180 -ErrorAction SilentlyContinue).WorkingSet64/1MB))MB"
