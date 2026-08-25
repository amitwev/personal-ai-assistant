FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY Directory.Packages.props Directory.Build.props ./
COPY tests/Assistant.WireMock/Assistant.WireMock.csproj tests/Assistant.WireMock/
RUN dotnet restore tests/Assistant.WireMock/Assistant.WireMock.csproj

COPY tests/Assistant.WireMock/ tests/Assistant.WireMock/
RUN dotnet publish tests/Assistant.WireMock/Assistant.WireMock.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Assistant.WireMock.dll"]
