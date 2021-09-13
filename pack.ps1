# Packs FVim for Windows platforms

param([string[]]$plat=("win7-x64","win-x64","win-arm"))
#param([string[]]$plat=("win7-x64","win-x64","linux-x64","osx-x64"))

New-Item -ItemType Directory -Force -Name publish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force bin\ -ErrorAction SilentlyContinue
Remove-Item publish\*

foreach($i in $plat) {
    dotnet publish -f net5.0 -c Release --self-contained -r $i fvim.fsproj
    if ($i -eq "win-x64") {
# replace the coreclr hosting exe with an icon-patched one
        Copy-Item lib/fvim-win10.exe bin/Release/net5.0/$i/publish/FVim.exe
# Avalonia 0.10.0-preview6 fix: manually copy ANGLE from win7-x64
        Copy-Item ~/.nuget/packages/avalonia.angle.windows.natives/2.1.0.2020091801/runtimes/win7-x64/native/av_libglesv2.dll bin/Release/net5.0/$i/publish/
    } elseif ($i -eq "win7-x64") {
        Copy-Item lib/fvim-win7.exe bin/Release/net5.0/$i/publish/FVim.exe
    } elseif ($i -eq "win-arm") {
        Copy-Item lib/fvim-win10-arm64.exe bin/Release/net5.0/$i/publish/FVim.exe
    }
    Compress-Archive -Path bin/Release/net5.0/$i/publish/* -DestinationPath publish/fvim-$i.zip -Force
}

