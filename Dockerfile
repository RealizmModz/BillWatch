# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS native-build

ARG LEPTONICA_VERSION=1.85.0

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        autoconf \
        automake \
        ca-certificates \
        curl \
        g++ \
        libjpeg62-turbo-dev \
        libpng-dev \
        libtiff-dev \
        libtool \
        make \
        pkg-config \
        zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /native-src

RUN curl --fail --location --silent --show-error \
        "https://github.com/DanBloomberg/leptonica/releases/download/${LEPTONICA_VERSION}/leptonica-${LEPTONICA_VERSION}.tar.gz" \
        --output leptonica.tar.gz \
    && tar --extract --gzip --file leptonica.tar.gz \
    && cd "leptonica-${LEPTONICA_VERSION}" \
    && ./configure --prefix=/usr/local --disable-static \
    && make --jobs="$(nproc)" \
    && make install DESTDIR=/native-root

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY BillWatch.Core/BillWatch.Core.csproj BillWatch.Core/
COPY BillWatch.API/BillWatch.API.csproj BillWatch.API/
RUN dotnet restore BillWatch.API/BillWatch.API.csproj

COPY BillWatch.Core/ BillWatch.Core/
COPY BillWatch.API/ BillWatch.API/

RUN dotnet publish BillWatch.API/BillWatch.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
        libtesseract-dev \
    && rm -rf /var/lib/apt/lists/*

COPY --from=native-build /native-root/usr/local/ /usr/local/

WORKDIR /app
COPY --from=build /app/publish/ ./

RUN ldconfig \
    && mkdir --parents /app/x64 /var/lib/billwatch/keys /var/lib/billwatch/statements \
    && ln --symbolic /usr/local/lib/libleptonica.so /app/x64/libleptonica-1.85.0.dll.so \
    && ln --symbolic "$(find /usr/lib -type f -name 'libtesseract.so.*' -print -quit)" /app/x64/libtesseract55.dll.so \
    && chown --recursive "$APP_UID:$APP_UID" /var/lib/billwatch

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent --header "Host: $AllowedHosts" http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "BillWatch.API.dll"]
