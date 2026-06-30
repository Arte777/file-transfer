$names = @('srmhost','ngcsvc','wuauclt','spoolsv','Runtime Broker','RAH Non Pro','dusmsvc','bthserv','agssrv','wlmserv','tablwrp','lsassist','ngcsvc')
foreach ($n in $names) {
    Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep 3
$remaining = Get-Process | Where-Object { $_.Name -match 'srmhost|ngcsvc|wuauclt|spoolsv|Runtime Broker|dusmsvc|bthserv|agssrv|wlmserv|tablwrp|lsassist' }
if ($remaining) {
    $remaining | Select-Object Id, Name | Format-Table -AutoSize
} else {
    Write-Output "All clean"
}
