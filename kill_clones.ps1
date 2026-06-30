$processes = Get-Process -Name 'Runtime Broker' -ErrorAction SilentlyContinue
foreach ($p in $processes) {
    try {
        $path = $p.MainModule.FileName
        if ($path -like '*Themes\RuntimeBroker*') {
            Write-Host ("Killing PID=" + $p.Id + " path=" + $path)
            $p.Kill()
            $p.WaitForExit(3000)
        }
    } catch {
        Write-Host ("Error: " + $_.Exception.Message)
    }
}
