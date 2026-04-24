# syntax=docker/dockerfile:1.7

# ----- build stage -----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NorcusSheetsManager/NorcusSheetsManager.csproj NorcusSheetsManager/
RUN dotnet restore NorcusSheetsManager/NorcusSheetsManager.csproj -r linux-x64

COPY NorcusSheetsManager/ NorcusSheetsManager/
RUN dotnet publish NorcusSheetsManager/NorcusSheetsManager.csproj \
        -c Release \
        -r linux-x64 \
        --no-self-contained \
        --no-restore \
        -o /app

# ----- runtime stage -----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Ghostscript (libgs + gs binary) is loaded by Magick.NET for PDF conversion
# and invoked directly by Converter.TryGetPdfPageCount.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ghostscript \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Default API port from ConfigLoader.APIServerSettings.Port
EXPOSE 4434

# NorcusSheetsManagerCfg.xml and NLog's debug.log/error.log live next to the
# binary. Mount a volume at /app (or rebind specific files) to persist them.
ENTRYPOINT ["./NorcusSheetsManager"]
