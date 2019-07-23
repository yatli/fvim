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
    rm -f ./*.deb

    pushd $PKG_ROOT
    cd ..
    mv publish fvim
    mkdir -p publish/usr/share
    mkdir -p publish/usr/share/applications
    mkdir -p publish/usr/share/icons/hicolor/48x48/apps/
    mkdir -p publish/usr/bin
    mv fvim publish/usr/share/
    popd
    cp lib/fvim-linux-launcher $PKG_ROOT/usr/bin/fvim
    cp Assets/fvim.png $PKG_ROOT/usr/share/icons/hicolor/48x48/apps/fvim.png
    cp lib/fvim.desktop $PKG_ROOT/usr/share/applications/fvim.desktop

    chmod +x $PKG_ROOT/usr/share/fvim/FVim
    chmod +x $PKG_ROOT/usr/bin/fvim
    fpm -s dir -t deb -n fvim -v $VERSION -C $PKG_ROOT

    mv *.deb publish/
}

function pack-osx-x64()
{
    rm -rf ./*.app
    rm -f ./*.zip

    pushd $PKG_ROOT
    cd ..
    mv publish fvim_pkg
    mkdir -p publish/Contents/
    mv fvim_pkg publish/Contents/MacOS
    mkdir -p publish/Contents/Resources/
    popd
    cp lib/fvim-osx-launcher $PKG_ROOT/Contents/MacOS/fvim-osx-launcher
    cp images/icon.icns $PKG_ROOT/Contents/Resources/fvim.icns
    cp lib/Info.plist $PKG_ROOT/Contents/Info.plist
    cp icons-icns/* $PKG_ROOT/Contents/Resources/
    rm -rf $PKG_ROOT/Contents/MacOS/icons

    chmod +x $PKG_ROOT/Contents/MacOS/FVim
    chmod +x $PKG_ROOT/Contents/MacOS/fvim-osx-launcher

    mv $PKG_ROOT FVim.app
    zip -r FVim.$VERSION.zip FVim.app
    rm -rf FVim.app
    mv FVim.$VERSION.zip publish/
}

pack-$PKG_TFM


