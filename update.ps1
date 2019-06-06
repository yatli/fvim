Stop-Process -Name "fvim"
dotnet build -c Release
cp bin/Release/netcoreapp2.2/FVim* C:/tools/fvim/
fvim
