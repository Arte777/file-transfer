Get-CimInstance Win32_Process -Filter "Name like '%Runtime%'" | Select-Object ProcessId, Name, ExecutablePath, CommandLine | Format-Table -AutoSize -Wrap
