#!/bin/bash
set -e

pushd src/SymbolCollector.Android/
dotnet publish -c Release
popd

pushd src/SymbolCollector.Server/
# Restore packages, builds it, runs smoke-test.
dotnet run -c Release -- --smoke-test
rm -rf publish
dotnet publish -c Release --no-build -o publish
pushd publish/
zip symbolcollector-server.zip ./*
popd
popd

# clean up old test results
find test -name "TestResults" -type d -prune -exec rm -rf '{}' +

pushd test/SymbolCollector.Server.Tests/
dotnet test -c Release --collect:"XPlat Code Coverage" --settings ../coverletArgs.runsettings
popd

pushd test/SymbolCollector.Core.Tests/
dotnet test -c Release --collect:"XPlat Code Coverage" --settings ../coverletArgs.runsettings
popd

pushd test/SymbolCollector.Console.Tests/
dotnet test -c Release --collect:"XPlat Code Coverage" --settings ../coverletArgs.runsettings
popd

pushd test/SymbolCollector.Android.UITests/
msbuild /restore /p:Configuration=Release /t:Build
# Don't run emulator tests on CI
if [ -z ${CI+x} ]; then
    pushd bin/Release
    export SYMBOL_COLLECTOR_APK=../../../../src/SymbolCollector.Android/bin/Release/net6.0-android/io.sentry.symbolcollector.android-Signed.apk
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

archs=(
    osx-x64
    linux-x64
    linux-musl-x64
    linux-arm
)
for arch in "${archs[@]}"; do
    dotnet publish -c release /p:PublishSingleFile=true --self-contained -r $arch -o publish-$arch
    zip -j symbolcollector-console-$arch.zip publish-$arch/SymbolCollector.Console
done

# Validate if SymbolCollector is starting on Mac OS X
if [[ "$OSTYPE" == "darwin"* ]]; then
    echo "Validating if generated SymbolCollector.Console starts..."
    cli=./publish-osx-x64/SymbolCollector.Console
    chmod +x $cli
    ./$cli --version -h
fi
popd
