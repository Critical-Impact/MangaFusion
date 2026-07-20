# MangaFusion

Self-hosted manga downloader and monitor. Primary source is **MangaDex**, with a source-agnostic
architecture so metadata and downloads can come from multiple providers. Web UI, runnable directly
or via Docker.

## Tech stack

- **.NET 10 / ASP.NET Core** host with **Autofac** DI
- **EF Core** (SQLite by default, Postgres-ready via provider swap) + **ASP.NET Core Identity** (roles)
- **Svelte + Vite** SPA, built into the host's `wwwroot` and served same-origin (REST + SignalR)
- **Hangfire** for scheduled monitoring and the download queue
- Single-container **Docker** deployment

## Solution layout

```
src/
  MangaFusion.Domain          entities, enums, value objects
  MangaFusion.Contracts       stable provider surface (source interfaces, IChapterWriter)
  MangaFusion.Application      use-case services, orchestration
  MangaFusion.Infrastructure  EF Core + Identity, migrations, jobs, storage
  MangaFusion.Sources.MangaDex the MangaDex provider
  MangaFusion.Web             ASP.NET host + composition root; serves the SPA
frontend/                     Svelte SPA (builds into src/MangaFusion.Web/wwwroot)
tests/                        Unit + integration tests
```

## Running in development

Two processes: the ASP.NET host (API) and the Vite dev server (SPA, proxies `/api` to the host).

```bash
# Terminal 1 — API on http://localhost:5253
dotnet run --project src/MangaFusion.Web

# Terminal 2 — SPA with hot reload on http://localhost:5173
cd frontend && npm install && npm run dev
```

Migrations are applied and an admin user is seeded automatically on first boot.
Default credentials (override via `Seed:AdminEmail` / `Seed:AdminPassword`):

```
admin@mangafusion.local  /  ChangeMe!123
```

To run the host serving the already-built SPA (single origin), run `npm run build` in `frontend/`
first, then start the API and open <http://localhost:5253>.

## Running with Docker (production)

```bash
docker compose up --build
# then open http://localhost:8080
```

Set `MF_ADMIN_EMAIL` / `MF_ADMIN_PASSWORD` to control the seeded admin. The SQLite database and
Data Protection keys persist in the `mf-data` volume; the downloaded library in `mf-library`.

## Administration

Sign in as the seeded admin and open **Admin** in the nav (`#/admin`). Three tabs:

- **Users** — create accounts, grant/revoke the Admin role, disable/enable (a disabled user can't
  sign in), or delete (which also removes that user's reading progress, follows, and notifications).
  The last administrator is protected: you can't demote, disable, or delete them, nor act on yourself.
- **Settings** — runtime settings that override `appsettings`/env without a restart: the monitor scan
  schedule (cron), default auto-download languages, default grace-period days, and **self-registration**
  (when off, only an admin can create accounts; default on).
- **Local** — create hand-curated series with manual metadata and import existing CBZ/folder files as
  their chapters (one file can map to several chapters). Files are picked up from the inbox at
  `LocalImport:InboxPath` (default `data/migrate-inbox`) and copied into the library; imported chapters read
  through the normal reader. Local series are badged and don't show download/scan controls.
- **Tasks** — a verbose background-job view (downloads + scans) with retry/requeue/delete.
- **Sources** — configure source credentials (e.g. the MangaDex account used for all lookups).

### Authorization model

The library is **shared** (files are downloaded once) with **per-user** reading progress, follows,
and notifications. Endpoint access:

| Area | Who |
|------|-----|
| Browse/search, read, follow, reading progress, add-to-library, queue downloads, per-series scan | any signed-in user |
| Source credentials, per-series group preferences & policy, full-library scan | **Admin** |
| User management, global settings | **Admin** |

> Note: add-to-library and queueing downloads are intentionally open to any signed-in user under the
> shared-library model. If you want those restricted to admins, that's a one-line policy change per
> endpoint — open an issue / say so.

## Database migrations

```bash
dotnet dotnet-ef migrations add <Name> \
  --project src/MangaFusion.Infrastructure \
  --startup-project src/MangaFusion.Web \
  --output-dir Persistence/Migrations
```

## Tests

```bash
dotnet test
```
