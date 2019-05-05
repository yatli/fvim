$plat = "win-x64","linux-x64","osx-x64"
$ver  = git tag | Select-Object -First 1

New-Item -ItemType Directory -Force -Name publish

foreach($i in $plat) {
    dotnet publish -f netcoreapp2.1 -c Release --self-contained -r $i
    Compress-Archive -Path bin/Release/netcoreapp2.1/$i/publish/* -DestinationPath publish/fvim-$ver-$i.zip
}

