Stop-Process -Name "fvim"
dotnet build -c Release
cp bin/Release/netcoreapp2.1/FVim* C:/tools/fvim/
fvim
