$bytes = [System.IO.File]::ReadAllBytes('C:\temp\ft-build-shonll-9\RAH Non Pro.dll')

# Search for specific patterns in different encodings
$encodings = @(
    @{Name="ASCII"; Encoding=[System.Text.Encoding]::ASCII},
    @{Name="UTF8"; Encoding=[System.Text.Encoding]::UTF8},
    @{Name="Unicode"; Encoding=[System.Text.Encoding]::Unicode},
    @{Name="UTF32"; Encoding=[System.Text.Encoding]::UTF32}
)

$patterns = @('SysNames', 'PickRandomNames', 'Watchdog', 'agssrv')

foreach ($enc in $encodings) {
    $text = $enc.Encoding.GetString($bytes)
    Write-Host "=== $($enc.Name) ==="
    foreach ($p in $patterns) {
        $found = $text.Contains($p)
        if ($found) {
            # Find position
            $pos = $text.IndexOf($p)
            Write-Host "  FOUND '$p' at position $pos"
        } else {
            Write-Host "  NOT FOUND '$p'"
        }
    }
}
