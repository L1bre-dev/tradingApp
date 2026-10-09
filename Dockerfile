# Two stages: the SDK image compiles the app, the much smaller ASP.NET image runs it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only: Docker caches this layer until a dependency changes.
COPY src/L1bre.Journal/L1bre.Journal.csproj src/L1bre.Journal/
COPY src/L1bre.Journal.Domain/L1bre.Journal.Domain.csproj src/L1bre.Journal.Domain/
COPY src/L1bre.Journal.CTrader/L1bre.Journal.CTrader.csproj src/L1bre.Journal.CTrader/
RUN dotnet restore src/L1bre.Journal/L1bre.Journal.csproj

COPY src/ src/
RUN dotnet publish src/L1bre.Journal/L1bre.Journal.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Links the image to the repository on GitHub, so it inherits the repository's (private) access.
LABEL org.opencontainers.image.source=https://github.com/L1bre-dev/tradingApp
WORKDIR /app
COPY --from=build /app .
# The deployment files travel with the image, so the VPS can extract them (see deploy/README.md).
COPY deploy/docker-compose.yml deploy/Caddyfile deploy/backup.sh deploy/.env.example /deploy/

# Run as the unprivileged "app" user that the .NET images provide.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "L1bre.Journal.dll"]
