# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

VaultVid — a self-hostable video-on-demand platform (README.md). ASP.NET Core 9 Blazor Server app; the assembly, root namespace and solution are all named `VideoHostingService`, so the repo folder name (`VaultVid`) and the code namespace differ.

Three projects: the web app at the repo root, `Core/` (entities plus `ApplicationDbContext`, shared with the worker), and `Worker/` (the ffmpeg transcoder). `Core` deliberately keeps `<RootNamespace>VideoHostingService</RootNamespace>` so the moved types kept their original namespaces.

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

The context lives in `Core/` but migrations stay in the web project, pinned by
`MigrationsAssembly("VideoHostingService")` in `Program.cs`. Run `dotnet ef` from the repo root.
`Data/DesignTimeDbContextFactory.cs` is what makes this work: without it EF builds the app host to
find the context, which fails when MinIO or the broker are unconfigured — i.e. whenever you are
actually adding a migration.

`Core/` and `Worker/` are nested inside the web project's directory, so `VideoHostingService.csproj`
carries `<Compile Remove="Core\**;Worker\**" />` (and the same for Content/None/EmbeddedResource).
Without those the Web SDK's default globs compile the other two projects' files into the web
assembly, which fails with CS8802 on the two `Program.cs` files.

Migrations are applied automatically at startup by `db.Database.Migrate()` at the end of `Program.cs` — do not add a separate migration step to deployment.

## Current state

Early WIP, pre-release, no production data. The Blazor UI, auth wiring and upload path were
reworked, and the transcoding pipeline was built out (queue, worker, HLS, media proxy).

**The EF migration in `Migrations/` predates the current model and must be regenerated before
the app will start** (`Program.cs` applies migrations on boot). Since there is no deployed data,
delete `Migrations/` and run `dotnet ef migrations add InitialCreate`. The model changed shape:
every entity now carries a `UserId` (string, matching `IdentityUser.Id`), `VideoLike.VoteSense`
is the `VoteSense` enum rather than `int`, `Video.PublicId` is uniquely indexed, `Video` gained
the transcoding columns (`Status`, source dimensions, `MasterPlaylistObjectName`,
`ProcessingError`, `ProcessingStartedAt`) alongside the new `VideoRendition` table, and FKs and
one-vote-per-user indexes are declared in `ApplicationDbContext.OnModelCreating`.

There is no .NET SDK in the Claude Code web sandbox (the download host is blocked by the network
policy), so changes made there are unbuilt — run `dotnet build` locally before trusting them.

## Architecture

**Data access.** `Models/Identity/ApplicationDbContext` (in `Core/Areas/Identity/Data/IdentityDbContext.cs`) is the single unified context: it extends `IdentityDbContext<IdentityUser>` *and* owns the domain `DbSet`s (Videos, VideoRenditions, VideoComments, VideoLikes, CommentLikes, Playlists). The web app and the worker share this one context — do not add a second one (a leftover duplicate, `Models/VideoServiceContext.cs`, was deleted for exactly this reason). Only the web app calls `Migrate()`.

**Services layer.** `Services/*Service.cs` each declare their interface and implementation in one file, use primary-constructor injection of `ApplicationDbContext`, and are registered scoped in `Program.cs`. Razor components call these directly via `@inject` — there is no controller or API layer. Service methods take the acting user's id and return `false` rather than throwing when the caller doesn't own the row; components pass the id from the `Task<AuthenticationState>` cascading parameter.

**Storage.** Video and thumbnail bytes go to MinIO (S3-compatible), everything else to Postgres. `VideoService` writes to a single bucket (`ObjectStorage:BucketName`) under the `videos/` and `thumbnails/` prefixes and creates it on demand; the worker writes HLS output under `hls/{videoId:N}/`. Every key is built from `Core/Models/MediaKeys.cs` so the two sides cannot drift. MinIO registration is guarded — a missing `ObjectStorage` config section logs to stderr and skips `AddMinio` rather than failing startup, so `dotnet ef` works without MinIO running (see commit `de9a113`), but `IMinioClient` then fails to resolve at request time.

**Media URLs.** Thumbnails are fetched straight from object storage via presigned URLs from `IMediaUrlService`, which holds a *second* Minio client built against `ObjectStorage:PublicEndpoint`. The signature covers the host, so a URL signed against the compose-internal `minio:9000` cannot be rewritten for the browser — `PublicEndpoint` must be set to an externally reachable address or every thumbnail and video 404s.

**Uploads.** `IBrowserFile.OpenReadStream` is forward-only, so `VideoService` buffers the first bytes, validates them against the extension (`ImageValidator` / `VideoValidator`, which take a header span), then replays them via `PrefixedStream`; `ProgressStream` wraps that to report throttled progress. Failures throw `InvalidFileException`. Objects are uploaded before the row is saved and removed again if the save fails, so an abandoned form leaves nothing behind. Size caps come from `MaxUploadSizes`, bound via `IOptions` (the fields are `long` — the 2 GiB default overflows `int`).

**Playback.** HLS is *not* presigned — a player follows relative URIs to child playlists and segments, which cannot carry a signature. `Endpoints/MediaEndpoints.cs` proxies `GET /media/{publicId:guid}/{**path}` from MinIO instead, restricted to `.m3u8` and `.ts` so the raw source under `videos/` stays unreachable. Playlists are written with relative URIs so they need no rewriting. `Watch.razor` points Vidstack at `/media/{publicId}/master.m3u8`.

**Transcoding.** Jobs are split by `TranscodeStage`: `Required` (360p/480p) and `Optional` (everything above), each on its own durable queue and its own worker deployment (`transcoder` / `transcoder-optional`), so a viewer-facing job never queues behind a long 4K encode. The worker publishes the optional job itself once the video goes Ready, which is why the queue client lives in `Core/Services/TranscodeQueue.cs` rather than the web project. `TranscodeWorkerOptions.Lanes` is a comma-separated *string*, not an array — configuration providers merge arrays by index, so an env var setting element 0 would leave a JSON element 1 in place and a worker asked for one lane would serve both.

`VideoService.CreateVideoAsync` publishes a `TranscodeRequest` after the row is saved — deliberately *outside* the object-rollback `catch`, so a broker outage does not discard a good upload; `Services/TranscodeSweeper.cs` re-publishes anything left `Pending` or stale-`Processing`. `Worker/` consumes the queue with prefetch 1 and walks `TranscodeLadder.Plan(sourceHeight)`, which caps the ladder at the source resolution and puts the required 360p/480p rungs first. The video flips to `Ready` the moment those exist; higher rungs keep going and the master playlist is rewritten after each. Both the queue registration (`RabbitMq` section) and MinIO are guarded, so the app still starts — and `dotnet ef` still works — with neither running.

**Caching.** Redis is wired as `IDistributedCache`; `Services/RedisCacheService.cs` is a JSON-serializing wrapper, registered scoped and used by the media proxy to cache the `PublicId → Id` lookup that every segment request repeats.

**Data protection keys** are persisted to `/vaultvid/keys` (a Docker volume) with application name `vaultvid` — required to avoid antiforgery-token exceptions across restarts and replicas (commit `bc892d4`).

**UI.** Two parallel UI stacks coexist: Blazor components under `Components/` (`Home`, `Upload`, `Watch` at `/video/{PublicId:guid}`, plus `Comment`/`CommentCreation`), and scaffolded Razor Pages for Identity under `Areas/Identity/Pages/` with shared layout in `Pages/Shared/`. Both are routed — `MapRazorComponents` and `MapRazorPages`. The whole router is interactive (`<Routes @rendermode="InteractiveServer" />` in `App.razor`); without that the app renders as static SSR and no form, file input or click handler does anything. Component-scoped `<Component>.razor.js` files are ES modules and only run if the component imports them by path via `IJSObjectReference` — they are not auto-loaded. Bootstrap is vendored in `wwwroot/lib`.

**Auth.** `AddDefaultIdentity` + `UseAuthentication`/`UseAuthorization` + `AddCascadingAuthenticationState`, with `<AuthorizeRouteView>` in `Routes.razor` and `RedirectToLogin` for anonymous visitors. `Identity:RequireConfirmedAccount` is configurable because no `IEmailSender` is wired up — it is false in Development, where confirmation would otherwise lock every new account out. Logout is a form POST to the Identity Razor Page, not a link.

**Not built yet:** a dead-letter exchange for jobs that exhaust their retries (they are acked and the row is marked `Failed` with a reason instead), and per-video authorisation on the media proxy, which is anonymous like the presigned URLs it replaced.
