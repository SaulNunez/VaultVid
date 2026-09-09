# Build stage. The build context is the repository root, so that Core can be restored too.
# Built with the 10.0 SDK, which still targets net9.0: the 9.0 SDK image's JIT crashes csc
# with SIGILL (exit 132) on arm64 hypervisors such as OrbStack on Apple Silicon. The runtime
# stage below stays on 9.0, so the shipped app is unaffected.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Worker/VideoHostingService.Worker.csproj Worker/
COPY Core/VideoHostingService.Core.csproj Core/
RUN dotnet restore Worker/VideoHostingService.Worker.csproj

COPY Worker/ Worker/
COPY Core/ Core/
RUN dotnet publish Worker/VideoHostingService.Worker.csproj -c Release -o /app/out

# Runtime stage.
# On the 9.0 runtime image the worker dies with SIGILL (exit 132) part-way through a large encode
# on arm64 hypervisors such as OrbStack on Apple Silicon - the same arm64 codegen fault that stops
# the 9.0 SDK image from compiling. The 10.0 runtime is unaffected and runs this net9.0 app via
# roll-forward. On x86 hosts either image works, so this is safe to keep.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ENV DOTNET_ROLL_FORWARD=LatestMajor
WORKDIR /app

# The whole point of a separate worker image: ffmpeg and its codecs stay out of the web image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "VideoHostingService.Worker.dll"]
