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

Early WIP, pre-release, no production data. The Blazor UI, auth wiring and upload path were
reworked; transcoding is still unbuilt.

**The EF migration in `Migrations/` predates the current model and must be regenerated before
the app will start** (`Program.cs` applies migrations on boot). Since there is no deployed data,
delete `Migrations/` and run `dotnet ef migrations add InitialCreate`. The model changed shape:
every entity now carries a `UserId` (string, matching `IdentityUser.Id`), `VideoLike.VoteSense`
is the `VoteSense` enum rather than `int`, `Video.PublicId` is uniquely indexed, and FKs and
one-vote-per-user indexes are declared in `ApplicationDbContext.OnModelCreating`.

There is no .NET SDK in the Claude Code web sandbox (the download host is blocked by the network
policy), so changes made there are unbuilt — run `dotnet build` locally before trusting them.

## Architecture

**Data access.** `Models/Identity/ApplicationDbContext` (in `Areas/Identity/Data/IdentityDbContext.cs`) is the single unified context: it extends `IdentityDbContext<IdentityUser>` *and* owns the domain `DbSet`s (Videos, VideoComments, VideoLikes, CommentLikes, Playlists). `Models/VideoServiceContext.cs` is a leftover duplicate from before contexts were merged — it is not registered in DI and not covered by migrations; do not add to it.

**Services layer.** `Services/*Service.cs` each declare their interface and implementation in one file, use primary-constructor injection of `ApplicationDbContext`, and are registered scoped in `Program.cs`. Razor components call these directly via `@inject` — there is no controller or API layer. Service methods take the acting user's id and return `false` rather than throwing when the caller doesn't own the row; components pass the id from the `Task<AuthenticationState>` cascading parameter.

**Storage.** Video and thumbnail bytes go to MinIO (S3-compatible), everything else to Postgres. `VideoService` writes to a single bucket (`ObjectStorage:BucketName`) under `videos/` and `thumbnails/` prefixes and creates it on demand. MinIO registration is guarded — a missing `ObjectStorage` config section logs to stderr and skips `AddMinio` rather than failing startup, so `dotnet ef` works without MinIO running (see commit `de9a113`), but `IMinioClient` then fails to resolve at request time.

**Media URLs.** Browsers fetch media straight from object storage via presigned URLs from `IMediaUrlService`, which holds a *second* Minio client built against `ObjectStorage:PublicEndpoint`. The signature covers the host, so a URL signed against the compose-internal `minio:9000` cannot be rewritten for the browser — `PublicEndpoint` must be set to an externally reachable address or every thumbnail and video 404s.

**Uploads.** `IBrowserFile.OpenReadStream` is forward-only, so `VideoService` buffers the first bytes, validates them against the extension (`ImageValidator` / `VideoValidator`, which take a header span), then replays them via `PrefixedStream`; `ProgressStream` wraps that to report throttled progress. Failures throw `InvalidFileException`. Objects are uploaded before the row is saved and removed again if the save fails, so an abandoned form leaves nothing behind. Size caps come from `MaxUploadSizes`, bound via `IOptions` (the fields are `long` — the 2 GiB default overflows `int`).

**Caching.** Redis is wired as `IDistributedCache`; `Services/RedisCacheService.cs` is a JSON-serializing wrapper but is not registered in DI or used yet.

**Data protection keys** are persisted to `/vaultvid/keys` (a Docker volume) with application name `vaultvid` — required to avoid antiforgery-token exceptions across restarts and replicas (commit `bc892d4`).

**UI.** Two parallel UI stacks coexist: Blazor components under `Components/` (`Home`, `Upload`, `Watch` at `/video/{PublicId:guid}`, plus `Comment`/`CommentCreation`), and scaffolded Razor Pages for Identity under `Areas/Identity/Pages/` with shared layout in `Pages/Shared/`. Both are routed — `MapRazorComponents` and `MapRazorPages`. The whole router is interactive (`<Routes @rendermode="InteractiveServer" />` in `App.razor`); without that the app renders as static SSR and no form, file input or click handler does anything. Component-scoped `<Component>.razor.js` files are ES modules and only run if the component imports them by path via `IJSObjectReference` — they are not auto-loaded. Bootstrap is vendored in `wwwroot/lib`.

**Auth.** `AddDefaultIdentity` + `UseAuthentication`/`UseAuthorization` + `AddCascadingAuthenticationState`, with `<AuthorizeRouteView>` in `Routes.razor` and `RedirectToLogin` for anonymous visitors. `Identity:RequireConfirmedAccount` is configurable because no `IEmailSender` is wired up — it is false in Development, where confirmation would otherwise lock every new account out. Logout is a form POST to the Identity Razor Page, not a link.

**Planned but not built:** transcoding. `docker-compose.yaml` already runs RabbitMQ for the future worker queue described in README.md; nothing in the app publishes to it yet.
