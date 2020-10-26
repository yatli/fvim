#!/bin/bash

SEMVER=`dotnet-gitversion /output json /showvariable SemVer`
COMMIT=`dotnet-gitversion /output json /showvariable ShortSha`

echo "v$SEMVER+$COMMIT"
