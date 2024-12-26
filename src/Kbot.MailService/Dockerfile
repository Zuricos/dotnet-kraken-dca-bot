FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["Kbot.Common/Kbot.Common.csproj", "Kbot.Common/"]
COPY ["Kbot.MailService/Kbot.MailService.csproj", "Kbot.MailService/"]
RUN dotnet restore "Kbot.MailService/Kbot.MailService.csproj"

COPY . .
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
RUN chown -R app:app /app/logs
USER app
ENTRYPOINT ["dotnet", "Kbot.MailService.dll"]