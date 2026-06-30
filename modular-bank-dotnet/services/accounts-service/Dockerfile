FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy solution file and project files
COPY ["services/accounts-service/AccountsService.csproj", "services/accounts-service/"]

# Restore dependencies
RUN dotnet restore "services/accounts-service/AccountsService.csproj"

# Copy source
COPY ["services/accounts-service/", "services/accounts-service/"]

# Build
WORKDIR /src/services/accounts-service
RUN dotnet build "AccountsService.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "AccountsService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FinBank.AccountsService.dll"]
