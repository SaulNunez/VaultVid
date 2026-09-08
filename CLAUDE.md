# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

VaultVid — a self-hostable video-on-demand platform (README.md). ASP.NET Core 9 Blazor Server app; the assembly, root namespace and solution are all named `VideoHostingService`, so the repo folder name (`VaultVid`) and the code namespace differ.

## Build and run

```bash
dotnet build                       # requires .NET 9 SDK
dotnet run                         # needs Postgres, MinIO and Redis reachable
docker compose up --build          # full stack; reads .env for the vars below
```

`docker-compose.yaml` requires `DATABASE_NAME`, `DATABASE_PASSWORD`, `OBJECT_STORAGE_ACCESS_KEY`, `OBJECT_STORAGE_SECRET_KEY`.

There is no test project and no linter configured.

### Migrations

```bash
dotnet ef migrations add <Name>    # ApplicationDbContext is the only context to target
```

Migrations are applied automatically at startup by `db.Database.Migrate()` at the end of `Program.cs` — do not add a separate migration step to deployment.

## Current state

This is an early WIP. Several files do **not compile** as committed — notably `Services/VideoService.cs` (misspelled `WtihObject`, `thumbnailFileStream`/`thumbnailSize` referenced inside `UploadVideo`, `IVideoService` signatures out of sync with the implementations) and `Components/Pages/Upload.razor` (`imageFile`/`maxFileSize` undefined, `await` in a `void` method). `ProgressReport`, used by the upload-progress API, is referenced but never defined. Expect to fix build errors adjacent to whatever you touch; don't assume a red build means you broke it.

## Architecture

**Data access.** `Models/Identity/ApplicationDbContext` (in `Areas/Identity/Data/IdentityDbContext.cs`) is the single unified context: it extends `IdentityDbContext<IdentityUser>` *and* owns the domain `DbSet`s (Videos, VideoComments, VideoLikes, CommentLikes, Playlists). `Models/VideoServiceContext.cs` is a leftover duplicate from before contexts were merged — it is not registered in DI and not covered by migrations; do not add to it.

**Services layer.** `Services/*Service.cs` each declare their interface and implementation in one file, use primary-constructor injection of `ApplicationDbContext`, and are registered scoped in `Program.cs`. Razor components call these directly via `@inject` — there is no controller or API layer.

**Storage.** Video and thumbnail bytes go to MinIO (S3-compatible), everything else to Postgres. `VideoService` writes to a single `vaultvid` bucket under `videos/` and `thumbnails/` prefixes and creates the bucket on demand; the `videoBucketName`/`thumbnailBucketName` constants are vestigial. MinIO registration is guarded — a missing `ObjectStorage` config section logs to stderr and skips `AddMinio` rather than failing startup, so `dotnet ef` works without MinIO running (see commit `de9a113`), but `IMinioClient` then fails to resolve at request time.

**Uploads.** Files are validated by magic-byte header against their extension (`Utilities/ImageExtensions.cs` → `ImageValidator`, `Utilities/VideoExtensions.cs` → `VideoValidator`; class names don't match file names). Failures throw `InvalidFileException`. Size caps come from the `MaxUploadSizes` config section, read ad hoc via `IConfiguration` inside `VideoService` rather than bound via options.

**Caching.** Redis is wired as `IDistributedCache`; `Services/RedisCacheService.cs` is a JSON-serializing wrapper but is not registered in DI or used yet.

**Data protection keys** are persisted to `/vaultvid/keys` (a Docker volume) with application name `vaultvid` — required to avoid antiforgery-token exceptions across restarts and replicas (commit `bc892d4`).

**UI.** Two parallel UI stacks coexist: Blazor interactive-server components under `Components/` (`Home`, `Upload`, `Video` at `/video/{Id:Guid}`, plus `Comment`/`CommentCreation`), and scaffolded Razor Pages for Identity under `Areas/Identity/Pages/` with shared layout in `Pages/Shared/`. Component-scoped JS lives next to its component as `<Component>.razor.js`. Bootstrap is vendored in `wwwroot/lib`.

**Planned but not built:** transcoding. `docker-compose.yaml` already runs RabbitMQ for the future worker queue described in README.md; nothing in the app publishes to it yet.
