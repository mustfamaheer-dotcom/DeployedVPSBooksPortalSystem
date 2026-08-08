# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PrintingBooksPortal/ PrintingBooksPortal/
RUN dotnet restore PrintingBooksPortal/PrintingBooksPortal.csproj --verbosity quiet \
    && dotnet publish PrintingBooksPortal/PrintingBooksPortal.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
VOLUME ["/app/App_Data", "/app/SecurePrints", "/root/.aspnet/DataProtection-Keys"]
ENTRYPOINT ["dotnet", "PrintingBooksPortal.dll"]
