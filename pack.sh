#!/bin/bash
# usage: 
#     pack.sh linux-x64
#     pack.sh osx-x64

mkdir -p publish
rm -rf bin
rm -ir publish/*

PKG_TFM=$1
VERSION=$(git describe)
VERSION=${VERSION:1}
PKG_ROOT="bin/Release/netcoreapp3.0/$PKG_TFM/publish"

sed -i '4i    <add key="Avalonia Nightly" value="https://www.myget.org/F/avalonia-ci/api/v2" />' ~/.nuget/NuGet/NuGet.Config
dotnet publish -f netcoreapp3.0 -c Release --self-contained -r $PKG_TFM


# references:
# https://developer.gnome.org/integration-guide/stable/desktop-files.html.en
# https://developer.gnome.org/integration-guide/stable/icons.html.en
# https://developer.gnome.org/icon-theme-spec/#file_formats
# https://wiki.archlinux.org/index.php/desktop_entries#File_example
function pack-linux-x64()
{
    rm ./*.deb

    pushd $PKG_ROOT
    cd ..
    mv publish fvim
    mkdir -p publish/usr/share
    mkdir -p publish/usr/share/applications
    mkdir -p publish/usr/share/icons/hicolor/48x48/apps/
    mkdir -p publish/usr/bin
    mv fvim publish/usr/share/
    popd
    cp lib/fvim $PKG_ROOT/usr/bin/fvim
    cp Assets/fvim.ico $PKG_ROOT/usr/share/icons/hicolor/48x48/apps/fvim.ico
    cp lib/fvim.desktop $PKG_ROOT/usr/share/applications/fvim.desktop

    fpm -s dir -t deb -n fvim -v $VERSION -C $PKG_ROOT

    mv *.deb publish/
}

function pack-osx-x64()
{
    echo "not supported"
}

pack-$PKG_TFM


