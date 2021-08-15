Stop-Process -Name "fvim"
dotnet build -c Release fvim.fsproj
cp bin/Release/netcoreapp3.1/FVim* C:/tools/fvim/
cp lib/fvim-win10.exe C:/tools/fvim/FVim.exe
fvim
