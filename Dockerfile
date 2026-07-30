# Usa la imagen SDK de .NET 8 para compilar la aplicación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["InmoAlerts.csproj", "./"]
RUN dotnet restore "InmoAlerts.csproj"
COPY . .
RUN dotnet publish "InmoAlerts.csproj" -c Release -o /app/publish

# Usa la imagen oficial de Playwright para .NET como base
FROM mcr.microsoft.com/playwright/dotnet:v1.44.0-jammy AS final
WORKDIR /app

# Instalar xvfb para simular monitor
RUN apt-get update && apt-get install -y xvfb && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Exponer el puerto que usará Railway
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Comando de arranque usando xvfb-run para engañar a DataDome
ENTRYPOINT ["xvfb-run", "--auto-servernum", "--server-args=-screen 0 1920x1080x24", "dotnet", "InmoAlerts.dll"]
