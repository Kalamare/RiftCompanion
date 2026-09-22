FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY native/Directory.Build.props native/Directory.Build.props
COPY native/Rift.Core/ native/Rift.Core/
COPY server/ server/
RUN dotnet publish server/Rift.Server/Rift.Server.csproj -c Release -o /out/api -p:RestoreLockedMode=true \
 && dotnet publish server/Rift.Worker/Rift.Worker.csproj -c Release -o /out/worker -p:RestoreLockedMode=true \
 && dotnet publish server/Rift.ServerChecks/Rift.ServerChecks.csproj -c Release -o /out/checks -p:RestoreLockedMode=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Rift.Server.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "Rift.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS checks
WORKDIR /app
COPY --from=build /out/checks .
USER $APP_UID
ENTRYPOINT ["dotnet", "Rift.ServerChecks.dll"]
