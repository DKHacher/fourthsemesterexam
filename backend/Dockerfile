# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything into container (from inside backend/)
COPY . ./

# Restore & Build
RUN dotnet restore "backend.csproj"
RUN dotnet build "backend.csproj" -c Release -o /app/build

# Publish Stage
FROM build AS publish
RUN dotnet publish "backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

COPY --from=publish /app/publish .
COPY ImageToTest.jpg ./ImageToTest.jpg
ENTRYPOINT ["dotnet", "backend.dll"]
