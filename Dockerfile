FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/Zwischenzug.Cli/Zwischenzug.Cli.csproj
RUN dotnet publish src/Zwischenzug.Cli/Zwischenzug.Cli.csproj -c Release --no-restore --self-contained false -p:UseAppHost=false -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Zwischenzug.Cli.dll"]
CMD ["uci"]
