#!/bin/bash
set -e

# Patch src/SymbolCollector.Android/Properties/AndroidManifest.xml
# Line: <meta-data android:name="io.sentry.symbol-collector" android:value="" />
# To add the correct endpoint, from env var at build time
pushd src/SymbolCollector.Android/
msbuild /restore /p:Configuration=Release \
    /p:AndroidBuildApplicationPackage=true \
    /t:Clean\;Build\;SignAndroidPackage
popd

pushd src/SymbolCollector.Server/
dotnet build -c Release
popd

pushd test/SymbolCollector.Server.Tests/
dotnet test -c Release
popd

pushd test/SymbolCollector.Core.Tests/
dotnet test -c Release
popd

pushd src/SymbolCollector.Console/
dotnet build -c Release
popd
