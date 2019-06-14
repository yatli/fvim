Stop-Process -Name "fvim"
dotnet build -c Release
cp bin/Release/netcoreapp3.0/FVim* C:/tools/fvim/
fvim
