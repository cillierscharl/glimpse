FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY src/Glimpse/*.csproj .
RUN dotnet restore
COPY src/Glimpse/ .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app

COPY --from=build /app .

RUN mkdir -p /data /screenshots

ENV Screenshots__WatchPath=/screenshots
ENV Database__Path=/data/glimpse.db

EXPOSE 5123

ENTRYPOINT ["dotnet", "Glimpse.dll"]
