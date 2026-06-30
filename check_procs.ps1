Get-Process | Where-Object { $_.ProcessName -match 'Runtime|RAH|FileTransfer' } | Select-Object Id,ProcessName,StartTime,CPU | Format-Table -AutoSize
