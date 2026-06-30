FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files
COPY ["gateway/Gateway.csproj", "gateway/"]

# Restore dependencies
RUN dotnet restore "gateway/Gateway.csproj"

# Copy source
COPY ["gateway/", "gateway/"]

# Build
WORKDIR /src/gateway
RUN dotnet build "Gateway.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FinBank.Gateway.dll"]
