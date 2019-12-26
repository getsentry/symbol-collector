REM pushd src\SymbolCollector.Android\
REM msbuild /restore /p:Configuration=Release ^
REM     /p:AndroidBuildApplicationPackage=true ^
REM     /t:Clean;Build;SignAndroidPackage
REM if not errorlevel 0 exit /b %errorlevel%
REM popd
REM
REM pushd src\SymbolCollector.Server\
REM dotnet publish -c release -o server
REM if not errorlevel 0 exit /b %errorlevel%
REM popd

pushd test\SymbolCollector.Server.Tests\
dotnet test -c Release
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
popd

pushd test\SymbolCollector.Core.Tests\
dotnet test -c Release
if "%errorlevel%" NEQ "0" exit /b %errorlevel%
popd
REM
REM pushd src\SymbolCollector.Console\
REM :Artifacts are picked up by appveyor (see .appveyor.yml)
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r osx-x64 -o osx-x64
REM if not errorlevel 0 exit /b %errorlevel%
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-x64 -o linux-x64
REM if not errorlevel 0 exit /b %errorlevel%
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-musl-x64 -o linux-musl-x64
REM if not errorlevel 0 exit /b %errorlevel%
REM dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-arm -o linux-arm
REM if not errorlevel 0 exit /b %errorlevel%
REM popd
