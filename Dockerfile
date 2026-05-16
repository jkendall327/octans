# syntax=docker/dockerfile:1

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Octans.Client/Octans.Client.csproj", "Octans.Client/"]
COPY ["Octans.Core/Octans.Core.csproj", "Octans.Core/"]
COPY ["Octans.Data/Octans.Data.csproj", "Octans.Data/"]

RUN dotnet restore "Octans.Client/Octans.Client.csproj"

COPY . .
RUN dotnet publish "Octans.Client/Octans.Client.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    GlobalSettings__AppRoot=/data/octans

EXPOSE 8080
VOLUME ["/data/octans", "/app/keys"]

COPY --from=build /app/publish .

RUN mkdir -p /app/keys /data/octans \
    && chown -R "$APP_UID:$APP_UID" /app /data/octans

USER $APP_UID

ENTRYPOINT ["dotnet", "Octans.Client.dll"]
