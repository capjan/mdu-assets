# MDU Asset Server: Native AOT program on a minimal, non-root runtime image.
# Build from the Source folder: docker build -t mdu-assets Source
# Builds for the architecture of the Docker host; CI builds amd64 and arm64 on native runners.

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
RUN apt-get update \
    && apt-get install --yes --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY MduAssets/MduAssets.csproj MduAssets/
COPY MduAssets.Storage/MduAssets.Storage.csproj MduAssets.Storage/
RUN dotnet restore MduAssets/MduAssets.csproj -r linux-$(dpkg --print-architecture | sed 's/amd64/x64/')
COPY MduAssets/ MduAssets/
COPY MduAssets.Storage/ MduAssets.Storage/
RUN dotnet publish MduAssets/MduAssets.csproj --configuration Release --no-restore \
        -r linux-$(dpkg --print-architecture | sed 's/amd64/x64/') \
        -warnaserror:IL2026,IL2104,IL3050,IL3053 \
        -p:DebugType=none -p:DebugSymbols=false -p:StripSymbols=true \
        -o /app \
    && mkdir /data

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/ ./
# The chiseled image has no shell to create folders, so /data comes from the build stage, owned by the app user.
COPY --from=build --chown=app:app /data /data
USER app
ENV MDU_ASSETS_DATADIRECTORY=/data \
    ASPNETCORE_HTTP_PORTS=8080
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["/app/mdu-assets"]
