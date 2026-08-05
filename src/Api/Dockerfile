# Build context is the repository root.

# The SPA is built here and served by the API as static files, so there is exactly one origin in
# production. That keeps the deployed app on the same footing as the dev proxy — no CORS, and the
# SSE endpoint behaves identically in both — at the cost of a Node stage in this image.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json* ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HilmaAgent.slnx ./
COPY src/Core/HilmaAgent.Core.csproj src/Core/
COPY src/Infrastructure/HilmaAgent.Infrastructure.csproj src/Infrastructure/
COPY src/Api/HilmaAgent.Api.csproj src/Api/
COPY src/Worker/HilmaAgent.Worker.csproj src/Worker/
COPY tests/HilmaAgent.Tests/HilmaAgent.Tests.csproj tests/HilmaAgent.Tests/
RUN dotnet restore src/Api/HilmaAgent.Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/HilmaAgent.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HilmaAgent.Api.dll"]
