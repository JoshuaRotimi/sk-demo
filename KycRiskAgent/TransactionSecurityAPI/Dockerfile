# Use the official .NET runtime as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["KycRiskAgent/TransactionSecurityAPI/TransactionSecurityAPI.csproj", "TransactionSecurityAPI/"]
RUN dotnet restore "TransactionSecurityAPI/TransactionSecurityAPI.csproj"
COPY . .
WORKDIR "/src/KycRiskAgent/TransactionSecurityAPI"
RUN dotnet build "TransactionSecurityAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TransactionSecurityAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TransactionSecurityAPI.dll"]