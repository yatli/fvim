$icons = Get-ChildItem ./icons-intellisense
foreach($icon in $icons) {
    Write-Host "Converting $icon"
    & 'C:/Program Files/Inkscape/inkscape.com' --export-png=".\Assets\intellisense\$([System.IO.Path]::GetFileNameWithoutExtension($icon.Name)).png" -w 64 -h 64 $icon.FullName
}
