REM pushd src\SymbolCollector.Android\
REM msbuild /restore /p:Configuration=Release ^
REM     /p:AndroidBuildApplicationPackage=true ^
REM     /t:Clean;Build;SignAndroidPackage
REM popd
REM
REM pushd src\SymbolCollector.Server\
REM dotnet build -c Release
REM popd
REM
REM pushd test\SymbolCollector.Server.Tests\
REM dotnet test -c Release
REM popd
REM
REM pushd test\SymbolCollector.Core.Tests\
REM dotnet test -c Release
REM popd

pushd src\SymbolCollector.Console\
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r osx-x64 -o osx-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-x64 -o linux-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-musl-x64 -o linux-musl-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-arm -o linux-arm
popd
