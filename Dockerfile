# syntax=docker/dockerfile:1

FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

COPY NuGet.Config ./
COPY study-planner.sln ./
COPY backend/backend.csproj backend/
COPY backend/Directory.Build.props backend/
RUN dotnet restore backend/backend.csproj

COPY backend backend/
COPY frontend/static/favicon.ico frontend/static/favicon.ico
COPY BScEE_generic_plan.json ./
COPY --from=frontend-build /src/frontend/build backend/wwwroot/

RUN dotnet publish backend/backend.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    --no-self-contained \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=backend-build /app/publish ./
COPY BScEE_generic_plan.json ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "DTU_StudyPlanner.dll"]
