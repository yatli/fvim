$plat = "win7-x64","win-x64","linux-x64","osx-x64"

New-Item -ItemType Directory -Force -Name publish
Remove-Item publish\*
Invoke-Command { 
    nuget sources remove -Name "Avalonia Nightly"
    nuget sources add -Name "Avalonia Nightly" -Source "https://www.myget.org/F/avalonia-ci/api/v2" -NonInteractive 
} -ErrorAction SilentlyContinue

foreach($i in $plat) {
    dotnet publish -f netcoreapp2.2 -c Release --self-contained -r $i
    if ($i -eq "win-x64") {
# replace the coreclr hosting exe with an icon-patched one
        Copy-Item lib/fvim-win10.exe bin/Release/netcoreapp2.2/$i/publish/FVim.exe
    }
    Compress-Archive -Path bin/Release/netcoreapp2.2/$i/publish/* -DestinationPath publish/fvim-$i.zip -Force
}

