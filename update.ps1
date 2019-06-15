Stop-Process -Name "fvim"
dotnet build -c Release
cp bin/Release/netcoreapp3.0/FVim* C:/tools/fvim/
cp lib/fvim-win10.exe C:/tools/fvim/FVim.exe
fvim
