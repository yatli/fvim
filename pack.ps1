# Packs FVim for Windows platforms

param([string[]]$plat=("win7-x64","win-x64"))
#param([string[]]$plat=("win7-x64","win-x64","linux-x64","osx-x64"))

New-Item -ItemType Directory -Force -Name publish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force bin\ -ErrorAction SilentlyContinue
Remove-Item publish\*
Invoke-Command { 
    nuget sources remove -Name "Avalonia Nightly"
    nuget sources add -Name "Avalonia Nightly" -Source "https://www.myget.org/F/avalonia-ci/api/v2" -NonInteractive 
} -ErrorAction SilentlyContinue

foreach($i in $plat) {
    dotnet publish -f netcoreapp3.0 -c Release --self-contained -r $i
    if ($i -eq "win-x64") {
# replace the coreclr hosting exe with an icon-patched one
        Copy-Item lib/fvim-win10.exe bin/Release/netcoreapp3.0/$i/publish/FVim.exe
    }
    Compress-Archive -Path bin/Release/netcoreapp3.0/$i/publish/* -DestinationPath publish/fvim-$i.zip -Force
}

