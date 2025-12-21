FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY src/Glimpse/*.csproj .
RUN dotnet restore
COPY src/Glimpse/ .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app

# Install Tesseract OCR CLI
RUN apt-get update && apt-get install -y --no-install-recommends \
    tesseract-ocr \
    tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Create directories for data
RUN mkdir -p /data /screenshots

ENV Screenshots__WatchPath=/screenshots
ENV Database__Path=/data/glimpse.db

EXPOSE 5123

ENTRYPOINT ["dotnet", "Glimpse.dll"]
