# Builds the spike with Native AOT inside Linux and runs it on a minimal runtime image.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY global.json NetVipsAot.csproj ./
RUN dotnet restore -r linux-$(dpkg --print-architecture | sed 's/amd64/x64/')
COPY Program.cs ./
RUN dotnet publish -c Release -r linux-$(dpkg --print-architecture | sed 's/amd64/x64/') --no-restore -o /out 2>&1 | tee /publish.log \
    && echo "AOT warnings: $(grep -cE 'warning (IL|CS)' /publish.log || true)"

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
COPY --from=build /out/ /app/
WORKDIR /app
ENTRYPOINT ["/app/NetVipsAot"]
