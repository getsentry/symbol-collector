dotnet publish -c release /p:PublishSingleFile=true --self-contained -r osx-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-musl-x64
dotnet publish -c release /p:PublishSingleFile=true --self-contained -r linux-arm
