FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Glimpse/*.csproj .
RUN dotnet restore
COPY src/Glimpse/ .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app .

RUN mkdir -p /data /screenshots

ENV Screenshots__WatchPath=/screenshots
ENV Database__Path=/data/glimpse.db
# appsettings.json binds to localhost for native runs; inside the container we must
# listen on all interfaces (docker-compose publishes the port on 127.0.0.1 only).
ENV Kestrel__Endpoints__Http__Url=http://0.0.0.0:5123

EXPOSE 5123

ENTRYPOINT ["dotnet", "Glimpse.dll"]
