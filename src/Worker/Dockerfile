# Build context is the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HilmaAgent.slnx ./
COPY src/Core/HilmaAgent.Core.csproj src/Core/
COPY src/Infrastructure/HilmaAgent.Infrastructure.csproj src/Infrastructure/
COPY src/Api/HilmaAgent.Api.csproj src/Api/
COPY src/Worker/HilmaAgent.Worker.csproj src/Worker/
COPY tests/HilmaAgent.Tests/HilmaAgent.Tests.csproj tests/HilmaAgent.Tests/
RUN dotnet restore src/Worker/HilmaAgent.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Worker/HilmaAgent.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "HilmaAgent.Worker.dll"]
