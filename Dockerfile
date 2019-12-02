FROM mcr.microsoft.com/dotnet/core/sdk:3.0 AS build
WORKDIR /app

# Target and its dependencies
COPY *.props ./server/
COPY src/SymbolCollector.Server/. ./server/SymbolCollector.Server/
COPY src/SymbolCollector.Core/. ./server/SymbolCollector.Core/

# Build target
WORKDIR /app/server/SymbolCollector.Server/
RUN dotnet publish -c Release -o ../out

FROM mcr.microsoft.com/dotnet/core/aspnet:3.0 AS runtime
WORKDIR /app
COPY --from=build /app/server/out ./
ENTRYPOINT ["dotnet", "SymbolCollector.Server.dll"]
