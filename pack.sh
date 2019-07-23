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
dotnet publish -f netcoreapp3.0 -c Release --self-contained -r $PKG_TFM

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
    chmod +x $PKG_ROOT/usr/share/fvim/FVim
    cp lib/fvim $PKG_ROOT/usr/bin/fvim
    cp Assets/fvim.png $PKG_ROOT/usr/share/icons/hicolor/48x48/apps/fvim.png
    cp lib/fvim.desktop $PKG_ROOT/usr/share/applications/fvim.desktop

    fpm -s dir -t deb -n fvim -v $VERSION -C $PKG_ROOT

    mv *.deb publish/
}

function pack-osx-x64()
{
    rm -rf ./*.app
    rm ./*.zip

    pushd $PKG_ROOT
    cd ..
    mv publish fvim
    mkdir -p publish/Contents/
    mv fvim publish/Contents/MacOS
    chmod +x publish/Contents/MacOS/FVim
    mkdir -p publish/Contents/Resources/
    popd
    cp images/icon.icns $PKG_ROOT/Contents/Resources/fvim.icns
    cp lib/Info.plist $PKG_ROOT/Contents/Info.plist

    mv $PKG_ROOT FVim.app
    zip FVim.$VERSION.zip FVim.app
    rm -rf FVim.app
    mv FVim.$VERSION.zip publish/
}

pack-$PKG_TFM


