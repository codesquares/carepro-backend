# Use the official .NET 8 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj files and restore dependencies
COPY ["CarePro-Api/CarePro-Api.csproj", "CarePro-Api/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Domain/Domain.csproj", "Domain/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]

# Restore dependencies
RUN dotnet restore "CarePro-Api/CarePro-Api.csproj"

# Copy the entire source code
COPY . .

# Build the application
WORKDIR "/app/CarePro-Api"
RUN dotnet build "CarePro-Api.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "CarePro-Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official .NET 8 runtime image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create a non-root user for security
RUN groupadd -r carepro && useradd -r -g carepro carepro

# Install required packages for health checks and security
RUN apt-get update && apt-get install -y \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Copy the published application
COPY --from=build /app/publish .

# Create directories and set permissions
RUN mkdir -p /app/logs && \
    chown -R carepro:carepro /app

# Switch to non-root user
USER carepro

# Expose port 8080 (ASP.NET Core default for non-root)
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Add health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set the entry point
ENTRYPOINT ["dotnet", "CarePro-Api.dll"]