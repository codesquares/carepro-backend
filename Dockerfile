# Use the official .NET 8 SDK image for building (latest patch version)
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
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

# Use the official .NET 8 runtime image for the final stage (latest patch version)
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# Create a non-root user for security (Alpine version)
RUN addgroup -g 1001 -S carepro && \
    adduser -S carepro -G carepro -u 1001

# Install required packages for health checks and security (Alpine)
RUN apk add --no-cache curl

# Copy the published application
COPY --from=build /app/publish .

# Create directories and set permissions
RUN mkdir -p /app/logs && \
    chown -R carepro:carepro /app

# Switch to non-root user
USER carepro

# Expose port 5000 for production
EXPOSE 5000

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

# Add health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5000/swagger/index.html || exit 1

# Set the entry point
ENTRYPOINT ["dotnet", "CarePro-Api.dll"]