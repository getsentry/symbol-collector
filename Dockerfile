# This Dockerfile builds the Server component of Symbol Collector.

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /app

# Copy the whole solution into the first stage
COPY * ./server/

# Build target
WORKDIR /app/server/SymbolCollector.Server/
RUN dotnet publish -c Release -o ../out

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS runtime
WORKDIR /app
COPY --from=build /app/server/out ./
ENTRYPOINT ["dotnet", "SymbolCollector.Server.dll"]
