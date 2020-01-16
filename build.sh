#!/bin/bash
set -e

pushd src/SymbolCollector.Android/
msbuild /restore /p:Configuration=Release \
    /p:AndroidBuildApplicationPackage=true \
    /t:Clean\;Build\;SignAndroidPackage
popd

pushd src/SymbolCollector.Server/
# Restore packages, builds it, runs smoke-test.
dotnet run -c Release -- --smoke-test
popd

# clean up old test results
find test -name "TestResults" -type d -prune -exec rm -rf '{}' +

pushd test/SymbolCollector.Server.Tests/
dotnet test -c Release --collect:"XPlat Code Coverage" --settings ../coverletArgs.runsettings
popd

pushd test/SymbolCollector.Core.Tests/
dotnet test -c Release --collect:"XPlat Code Coverage" --settings ../coverletArgs.runsettings
popd

pushd test/SymbolCollector.Android.UITests/
msbuild /restore /p:Configuration=Release /t:Build
# Don't run emulator tests on Travis-CI
if [ -z ${TRAVIS_JOB_ID+x} ]; then
    pushd bin/Release
    export SYMBOL_COLLECTOR_APK=../../../../src/SymbolCollector.Android/bin/Release/io.sentry.symbol.collector.apk
    mono ../../tools/nunit/net35/nunit3-console.exe SymbolCollector.Android.UITests.dll
    unset SYMBOL_COLLECTOR_APK
    popd
fi
popd

pushd src/SymbolCollector.Console/
# Smoke test the console app
dotnet run -c release -- \
    --check ../../test/TestFiles/System.Net.Http.Native.dylib \
    | grep c5ff520a-e05c-3099-921e-a8229f808696 || echo -e "Failed testing console 'check' command"
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r osx-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-musl-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-arm
popd
