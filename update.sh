#!/bin/sh

pkill FVim
dotnet build -c Release fvim.fsproj
sudo rm -rf /usr/share/fvim/*
sudo cp -r bin/Release/net6.0/* /usr/share/fvim/
