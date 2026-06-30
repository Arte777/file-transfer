Get-CimInstance Win32_Process -Filter "Name like '%Runtime%'" | Select-Object ProcessId, Name, ExecutablePath | Format-Table -AutoSize -Wrap
