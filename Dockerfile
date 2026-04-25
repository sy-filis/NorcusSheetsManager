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

# Default API port from ApiServer.Url in appsettings.json.
EXPOSE 4434

# Mount points expected at runtime:
#
#   /sheets                       host folder containing the sheet-music tree.
#                                 In the mounted appsettings.json set
#                                 Converter.SheetsPath to "/sheets".
#
#   /app/appsettings.json         host config (paths, DB credentials, JWT key).
#                                 Bind-mount the file (read-only is fine).
#
#   /app/debug.log /app/error.log NLog rolling files. Bind-mount them or the
#                                 enclosing directory if you need persistence
#                                 across container restarts.
#
# Example:
#   docker run -d \
#       -v /host/sheets:/sheets \
#       -v /host/config/appsettings.json:/app/appsettings.json:ro \
#       -v /host/logs/debug.log:/app/debug.log \
#       -v /host/logs/error.log:/app/error.log \
#       -p 4434:4434 \
#       norcus-sheets-manager
VOLUME ["/sheets"]

ENTRYPOINT ["./NorcusSheetsManager"]
