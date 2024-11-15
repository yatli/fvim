Stop-Process -Name "fvim"
dotnet publish -f net8.0 -c Release --self-contained -r win-x64 fvim.fsproj
cp bin/Release/net8.0/win-x64/publish/FVim* C:/tools/fvim/
cp lib/fvim-win10.exe C:/tools/fvim/FVim.exe
cp fvim.vim C:/tools/fvim/fvim.vim
fvim
