Stop-Process -Name "fvim"
.\pack.ps1 -plat "win-x64"
Remove-Item -Recurse -Force C:/tools/fvim/*
Expand-Archive publish/fvim-win-x64.zip C:/tools/fvim/
cp lib/fvim-win10.exe C:/tools/fvim/FVim.exe
fvim
