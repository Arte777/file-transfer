$processes = Get-Process -Name 'Runtime Broker' -ErrorAction SilentlyContinue
Write-Host ("Found " + $processes.Count + " Runtime Broker processes")
foreach ($p in $processes) {
    try {
        $path = $p.MainModule.FileName
        Write-Host ("PID=" + $p.Id + " Path=" + $path)
        Write-Host ("  Contains Themes\RuntimeBroker? " + $path.Contains('Themes\RuntimeBroker'))
        if ($path -like '*Themes\RuntimeBroker*') {
            Write-Host ("  Killing PID=" + $p.Id)
            $p.Kill()
            $p.WaitForExit(3000)
            Write-Host ("  Killed")
        }
    } catch {
        Write-Host ("  Error: " + $_.Exception.Message)
    }
}
