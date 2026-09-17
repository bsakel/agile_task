# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f src/Host/OrderPlatform.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /repo

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/Host/OrderPlatform.Api/OrderPlatform.Api.csproj

# Pre-generate Wolverine handler code so the image needs no runtime compilation (ADR-0004, ADR-0021).
# The command only builds the host; it does not connect to the database.
RUN ConnectionStrings__orderplatform="Host=codegen;Database=codegen" \
    dotnet run --project src/Host/OrderPlatform.Api/OrderPlatform.Api.csproj --no-restore -c Release -- codegen write

RUN dotnet publish src/Host/OrderPlatform.Api/OrderPlatform.Api.csproj --no-restore -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Wolverine__TypeLoadMode=Static
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderPlatform.Api.dll"]
