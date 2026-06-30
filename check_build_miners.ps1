$buildDir = "C:\temp\ft-build-shonll-10"
Write-Host "=== Checking build-10 for miner files ==="
Get-ChildItem $buildDir -Recurse -Force | Where-Object { $_.Length -gt 1000000 -and $_.Extension -in '.exe','.dll' } | ForEach-Object {
    $name = $_.Name
    $len = $_.Length
    $path = $_.FullName
    Write-Host ("  {0,-30} {1,10} bytes - {2}" -f $name, $len, $path)
}
