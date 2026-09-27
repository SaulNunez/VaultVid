# Build stage
# Built with the 10.0 SDK, which still targets net9.0: the 9.0 SDK image's JIT crashes csc
# with SIGILL (exit 132) on arm64 hypervisors such as OrbStack on Apple Silicon. The runtime
# stage below stays on 9.0, so the shipped app is unaffected.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first so the restore layer is cached until a dependency actually changes.
COPY VideoHostingService.csproj ./
COPY Core/VideoHostingService.Core.csproj Core/
RUN dotnet restore VideoHostingService.csproj

COPY . ./
RUN dotnet publish VideoHostingService.csproj -c Release -o /app/out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "VideoHostingService.dll"]
