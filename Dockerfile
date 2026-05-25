# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 7272
EXPOSE 7273

# Creates a non-root user with an explicit UID and adds permission to access the /app folder
# For more info, please refer to https://aka.ms/containers-non-root
ARG UID=10001
RUN useradd \
    --create-home \
    --home-dir /app \
    --no-log-init \
    --shell /sbin/nologin \
    --uid "${UID}" \
    appuser \
    && mkdir -p /app/data \
    && chown -R ${UID}:${UID} /app/data
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["AnimeOverseer.Server/AnimeOverseer.Server.csproj", "AnimeOverseer.Server/"]
RUN dotnet restore "AnimeOverseer.Server/AnimeOverseer.Server.csproj"
COPY . .
WORKDIR "/src/AnimeOverseer.Server"
RUN dotnet build "AnimeOverseer.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AnimeOverseer.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AnimeOverseer.Server.dll"]