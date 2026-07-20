# syntax=docker/dockerfile:1

# --- Stage 1: build the Svelte SPA -----------------------------------------------------------
FROM node:24-alpine AS frontend
WORKDIR /fe
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
# Override the config's outDir (which points at the .NET project) to a local folder in this stage.
RUN npm run build -- --outDir dist --emptyOutDir

# --- Stage 2: restore, build & publish the .NET host -----------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG GIT_VERSION=local
WORKDIR /src

# Copy only what restore needs first, for layer caching.
COPY Directory.Build.props Directory.Packages.props MangaFusion.slnx ./
COPY src/MangaFusion.Domain/*.csproj src/MangaFusion.Domain/
COPY src/MangaFusion.Contracts/*.csproj src/MangaFusion.Contracts/
COPY src/MangaFusion.Application/*.csproj src/MangaFusion.Application/
COPY src/MangaFusion.Infrastructure/*.csproj src/MangaFusion.Infrastructure/
COPY src/MangaFusion.Sources.MangaDex/*.csproj src/MangaFusion.Sources.MangaDex/
COPY src/MangaFusion.Web/*.csproj src/MangaFusion.Web/
RUN dotnet restore src/MangaFusion.Web/MangaFusion.Web.csproj

# Copy the rest of the source and bundle the built SPA into wwwroot before publishing.
COPY src/ src/
COPY --from=frontend /fe/dist src/MangaFusion.Web/wwwroot
RUN dotnet publish src/MangaFusion.Web/MangaFusion.Web.csproj -c Release -o /app/publish /p:UseAppHost=false /p:GitVersion=${GIT_VERSION}

# --- Stage 3: runtime ------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# SQLite DB and Data Protection keys live here; mount a volume to persist them.
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "MangaFusion.Web.dll"]
