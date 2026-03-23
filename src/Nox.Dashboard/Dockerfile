FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Nox.Dashboard/Nox.Dashboard.csproj
RUN dotnet publish src/Nox.Dashboard/Nox.Dashboard.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Nox.Dashboard.dll"]
