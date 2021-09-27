Stop-Process -Name "fvim"
dotnet build -c Release fvim.fsproj
cp bin/Release/net5.0/FVim* C:/tools/fvim/
cp lib/fvim-win10.exe C:/tools/fvim/FVim.exe
cp fvim.vim C:/tools/fvim/fvim.vim
fvim
