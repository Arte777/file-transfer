$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
# Dump 200 bytes around SysNames position (75431-75631 in UTF8)
$start = 75400
$end = 75700
for ($i = $start; $i -lt $end; $i++) {
    $b = $bytes[$i]
    $c = if ($b -ge 32 -and $b -le 126) { [char]$b } else { '.' }
    Write-Host -NoNewline $c
}
Write-Host ""
