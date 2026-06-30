FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy solution and project files
COPY ["src/ModularBank/ModularBank.csproj", "src/ModularBank/"]
COPY ["tests/ModularBank.Tests/ModularBank.Tests.csproj", "tests/ModularBank.Tests/"]

# Restore dependencies
RUN dotnet restore "src/ModularBank/ModularBank.csproj"

# Copy source
COPY ["src/", "src/"]
COPY ["tests/", "tests/"]

# Build
WORKDIR /src/src/ModularBank
RUN dotnet build "ModularBank.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "ModularBank.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "ModularBank.dll"]
