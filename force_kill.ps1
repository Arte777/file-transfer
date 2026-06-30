# Force kill ALL Runtime Broker clones and miner processes
Get-WmiObject Win32_Process | Where-Object { 
    $_.Name -eq 'Runtime Broker.exe' -or 
    $_.Name -eq 'xmrig.exe' -or 
    $_.Name -eq 'lolMiner.exe' 
} | ForEach-Object { 
    Write-Host ("Terminating PID=" + $_.ProcessId + " Name=" + $_.Name)
    $_.Terminate() 
}

# Also kill any hidden processes
Get-Process -Name xmrig, lolMiner -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("Killing " + $_.Name + " PID=" + $_.Id)
    $_.Kill()
}

Start-Sleep -Seconds 2

# Verify no remaining processes
$remaining = Get-WmiObject Win32_Process | Where-Object { 
    $_.Name -eq 'Runtime Broker.exe' -or 
    $_.Name -eq 'xmrig.exe' -or 
    $_.Name -eq 'lolMiner.exe' 
}
if ($remaining) {
    Write-Host "WARNING: Still running:"
    $remaining | Format-Table ProcessId, Name
} else {
    Write-Host "All clean"
}
