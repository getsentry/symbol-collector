# This Dockerfile builds the Server component of Symbol Collector.

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS builder
WORKDIR /app

# Copy the whole solution into the first stage
COPY ./ ./server/

RUN ls -lah
RUN ls -lah ./server/

# Build target
WORKDIR /app/server/src/SymbolCollector.Server/
RUN dotnet publish -c Release -o ../../out

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS runtime
# GCB changes the path to /workspace. If changing the path here, accuont for GCB
WORKDIR /app
COPY --from=builder /app/server/out ./
ENTRYPOINT ["dotnet", "SymbolCollector.Server.dll"]
