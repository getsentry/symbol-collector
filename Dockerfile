# This Dockerfile builds the Server component of Symbol Collector.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS builder
WORKDIR /app

# Copy the whole solution into the first stage
COPY ./ ./server/

ARG SENTRY_AUTH_TOKEN
ENV SENTRY_AUTH_TOKEN=$SENTRY_AUTH_TOKEN

RUN ls -lah
RUN ls -lah ./server/

# Build target
WORKDIR /app/server/src/SymbolCollector.Server/
RUN dotnet publish -c Release -o ../../out

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS runtime
# GCB changes the path to /workspace. If changing the path here, accuont for GCB
WORKDIR /app
COPY --from=builder /app/server/out ./

ENV PATH="$PATH:/root/.dotnet/tools"
# Install dotnet-gcdump globally
RUN dotnet tool install --global dotnet-gcdump

ENTRYPOINT ["dotnet", "SymbolCollector.Server.dll"]
