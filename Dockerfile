#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# apline only if published as single file
# FROM arm64v8/alpine:3.19 AS base
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine3.19-arm64v8 AS base
WORKDIR /app
RUN apk upgrade --no-cache && apk add --no-cache postgresql-client bash openssl libgcc libstdc++ ncurses-libs

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GPIO-Control.csproj", "."]
RUN dotnet restore "./././GPIO-Control.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./GPIO-Control.csproj" -c $BUILD_CONFIGURATION -o /app/build -r linux-musl-arm64

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Publish as single file
# RUN dotnet publish "./GPIO-Control.csproj" -c $BUILD_CONFIGURATION -r linux-musl-arm64 -o /app/publish /p:UseAppHost=true /p:PublishSingleFile=true
RUN dotnet publish "./GPIO-Control.csproj" -c $BUILD_CONFIGURATION -r linux-musl-arm64 -o /app/publish /p:UseAppHost=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["./GPIO-Control"]