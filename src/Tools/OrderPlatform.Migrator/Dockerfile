# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f src/Tools/OrderPlatform.Migrator/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /repo

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/Tools/OrderPlatform.Migrator/OrderPlatform.Migrator.csproj
RUN dotnet publish src/Tools/OrderPlatform.Migrator/OrderPlatform.Migrator.csproj --no-restore -c Release -o /app /p:UseAppHost=false

# The Migrator shares the composition root (ASP.NET Core framework reference), hence the aspnet runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "OrderPlatform.Migrator.dll"]
