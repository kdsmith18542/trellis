# Multi-stage build for Trellis.Node
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Trellis.sln
RUN dotnet build src/Trellis.Node --configuration Release --no-restore
RUN dotnet publish src/Trellis.Node --configuration Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
EXPOSE 5000
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Trellis.Node.dll"]
