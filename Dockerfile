# syntax=docker/dockerfile:1

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Octans.sln", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Octans.Client/Octans.Client.csproj", "Octans.Client/"]
COPY ["Octans.Core/Octans.Core.csproj", "Octans.Core/"]
COPY ["Octans.Data/Octans.Data.csproj", "Octans.Data/"]
COPY ["Octans.Tests/Octans.Tests.csproj", "Octans.Tests/"]

RUN dotnet restore "Octans.Client/Octans.Client.csproj"

COPY . .
WORKDIR /src/Octans.Client
RUN dotnet publish "Octans.Client.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Octans.Client.dll"]
