FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files
COPY ["services/transfers-service/TransfersService.csproj", "services/transfers-service/"]

# Restore dependencies
RUN dotnet restore "services/transfers-service/TransfersService.csproj"

# Copy source
COPY ["services/transfers-service/", "services/transfers-service/"]

# Build
WORKDIR /src/services/transfers-service
RUN dotnet build "TransfersService.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "TransfersService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FinBank.TransfersService.dll"]
