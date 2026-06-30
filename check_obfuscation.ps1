$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')
$text = [System.Text.Encoding]::Unicode.GetString($bytes)
if ($text.Contains('AV-Obf')) { Write-Host 'FOUND AV-Obf' } else { Write-Host 'NOT FOUND AV-Obf' }
if ($text.Contains('SysNames')) { Write-Host 'FOUND SysNames' } else { Write-Host 'NOT FOUND SysNames' }
if ($text.Contains('ObfuscationKey')) { Write-Host 'FOUND ObfuscationKey' } else { Write-Host 'NOT FOUND ObfuscationKey' }
if ($text.Contains('PickRandomNames')) { Write-Host 'FOUND PickRandomNames' } else { Write-Host 'NOT FOUND PickRandomNames' }
