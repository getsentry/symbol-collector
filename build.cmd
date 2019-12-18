pushd src\SymbolCollector.Android\
msbuild /restore /p:Configuration=Release ^
    /p:AndroidBuildApplicationPackage=true ^
    /t:Clean;Build;SignAndroidPackage
popd

REM pushd src\SymbolCollector.Server\
REM dotnet publish -c release -o server
REM popd
REM
REM pushd test\SymbolCollector.Server.Tests\
REM dotnet test -c Release
REM popd
REM
REM pushd test\SymbolCollector.Core.Tests\
REM dotnet test -c Release
REM popd
REM
REM pushd src\SymbolCollector.Console\
REM :Artifacts are picked up by appveyor (see .appveyor.yml)
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r osx-x64 -o osx-x64
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-x64 -o linux-x64
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-musl-x64 -o linux-musl-x64
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-arm -o linux-arm
REM popd
