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

# clean up old test results
find test -name "TestResults" -type d -prune -exec rm -rf '{}' +

pushd test/SymbolCollector.Server.Tests/
dotnet test -c Release --collect:"XPlat Code Coverage" --settings ../coverletArgs.runsettings
popd

pushd test/SymbolCollector.Core.Tests/
dotnet test -c Release --collect:"XPlat Code Coverage" --settings ../coverletArgs.runsettings
popd

pushd src/SymbolCollector.Console/
# Smoke test the console app
dotnet run -c release -- \
    --check ../../test/TestFiles/System.Net.Http.Native.dylib \
    | grep c5ff520a-e05c-3099-921e-a8229f808696 || echo -e "Failed testing console 'release' command"
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r osx-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-musl-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-arm
popd
