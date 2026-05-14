FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["PG.StarWarsGame.LSP.Server/PG.StarWarsGame.LSP.Server.csproj", "PG.StarWarsGame.LSP.Server/"]
RUN dotnet restore "PG.StarWarsGame.LSP.Server/PG.StarWarsGame.LSP.Server.csproj"
COPY . .
WORKDIR "/src/PG.StarWarsGame.LSP.Server"
RUN dotnet build "./PG.StarWarsGame.LSP.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./PG.StarWarsGame.LSP.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PG.StarWarsGame.LSP.Server.dll"]
