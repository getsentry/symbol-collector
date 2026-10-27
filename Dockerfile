# This Dockerfile builds the Server component of Symbol Collector.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
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

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
# .NET on Linux trusts the OS CA store. Pull the latest ca-certificates so CA
# distrust updates ship even when the base image lags behind Ubuntu security.
RUN apt-get update \
    && apt-get install -y --only-upgrade --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
# GCB changes the path to /workspace. If changing the path here, accuont for GCB
WORKDIR /app
COPY --from=builder /app/server/out ./

ENV PATH="$PATH:/root/.dotnet/tools"
# Install dotnet-gcdump globally
RUN dotnet tool install --global dotnet-gcdump

ENTRYPOINT ["dotnet", "SymbolCollector.Server.dll"]
