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
COPY src/Kbot.MailService/Kbot.MailService.csproj Kbot.MailService/
RUN dotnet restore "Kbot.MailService/Kbot.MailService.csproj"

COPY src/ ./
WORKDIR "/src/Kbot.MailService"
RUN dotnet build "Kbot.MailService.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "Kbot.MailService.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER root
RUN mkdir /app/logs
RUN mkdir /app/state
RUN chown -R app:app /app/logs
RUN chown -R app:app /app/state
USER app
ENTRYPOINT ["dotnet", "Kbot.MailService.dll"]
