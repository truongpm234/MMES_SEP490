# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AMMS.sln .

COPY AMMS.API/*.csproj ./AMMS.API/
COPY AMMS.Application/*.csproj ./AMMS.Application/
COPY AMMS.Infrastructure/*.csproj ./AMMS.Infrastructure/
COPY AMMS.Shared/*.csproj ./AMMS.Shared/

RUN dotnet restore

COPY . .

WORKDIR /src/AMMS.API
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y \
    libreoffice \
    libreoffice-writer \
    poppler-utils \
    tesseract-ocr \
    tesseract-ocr-vie \
    tesseract-ocr-eng \
    fontconfig \
    fonts-dejavu \
    fonts-liberation \
    locales \
    && rm -rf /var/lib/apt/lists/*

ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

COPY --from=publish /app/publish .

RUN echo "{}" > appsettings.json

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV CONTRACT_COMPARE_KEEP_DEBUG_FILES=true

ENTRYPOINT ["dotnet", "AMMS.API.dll"]