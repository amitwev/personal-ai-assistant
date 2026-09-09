# runtime, not aspnet: Assistant.Worker is a Worker SDK app with no HTTP endpoint.
# Ubuntu, not Alpine: TimeSettings.Validate() calls TimeZoneInfo.FindSystemTimeZoneById, and
# this base image carries tzdata (Alpine's does not) plus ICU for the same reason.
# No HEALTHCHECK: the /tmp/heartbeat file spec 8 describes is F14 work that does not exist yet --
# a healthcheck watching a file nothing writes would restart-loop the container.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Packages.props Directory.Build.props ./
COPY src/Assistant.Contracts/Assistant.Contracts.csproj src/Assistant.Contracts/
COPY src/Assistant.Impl/Assistant.Impl.csproj src/Assistant.Impl/
COPY src/Assistant.Interfaces/Assistant.Interfaces.csproj src/Assistant.Interfaces/
COPY src/Assistant.Models/Assistant.Models.csproj src/Assistant.Models/
COPY src/Assistant.Repository/Assistant.Repository.csproj src/Assistant.Repository/
COPY src/Assistant.Worker/Assistant.Worker.csproj src/Assistant.Worker/
RUN dotnet restore src/Assistant.Worker/Assistant.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Assistant.Worker/Assistant.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Assistant.Worker.dll"]
