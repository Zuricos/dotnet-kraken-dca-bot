FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src

COPY nuget.config .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/Kbot.Common/Kbot.Common.csproj Kbot.Common/
COPY src/Kbot.DcaService/Kbot.DcaService.csproj Kbot.DcaService/
RUN dotnet restore "Kbot.DcaService/Kbot.DcaService.csproj"

COPY src/ ./
WORKDIR "/src/Kbot.DcaService"
RUN dotnet build "Kbot.DcaService.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "Kbot.DcaService.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER root
RUN mkdir /app/state
RUN mkdir /app/logs
RUN chown -R app:app /app/state
RUN chown -R app:app /app/logs
USER app
ENTRYPOINT ["dotnet", "Kbot.DcaService.dll"]
