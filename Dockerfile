#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM arm64v8/alpine:3.14 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Z-PumpControl_Raspi.csproj", "."]
RUN dotnet restore "./././Z-PumpControl_Raspi.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./Z-PumpControl_Raspi.csproj" -c $BUILD_CONFIGURATION -o /app/build -r linux-arm64

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Z-PumpControl_Raspi.csproj" -c $BUILD_CONFIGURATION -r linux-arm64 -o /app/publish /p:UseAppHost=true /p:PublishSingleFile=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["./Z-PumpControl_Raspi"]