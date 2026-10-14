# Build from the repository root: docker build -f services/api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/api/SmartRail.Api.csproj services/api/
RUN dotnet restore services/api/SmartRail.Api.csproj
COPY services/api/ services/api/
RUN dotnet publish services/api/SmartRail.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The aspnet image listens on port 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartRail.Api.dll"]
