pushd src\SymbolCollector.Android\
msbuild /restore /p:Configuration=Release ^
    /p:AndroidBuildApplicationPackage=true ^
    /t:Clean;Build;SignAndroidPackage
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
popd

pushd src\SymbolCollector.Server\
dotnet publish -c release -o server
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
popd

pushd test\SymbolCollector.Server.Tests\
dotnet test -c Release
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
popd

pushd test\SymbolCollector.Core.Tests\
dotnet test -c Release
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
popd

pushd src\SymbolCollector.Console\
:Artifacts are picked up by appveyor (see .appveyor.yml)
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r osx-x64 -o osx-x64
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-x64 -o linux-x64
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-musl-x64 -o linux-musl-x64
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-arm -o linux-arm
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
popd
