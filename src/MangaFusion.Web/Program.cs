using System.Security.Claims;
using Autofac;
using Microsoft.AspNetCore.DataProtection;
using Autofac.Extensions.DependencyInjection;
using Hangfire;
using Hangfire.Storage.SQLite;
using MangaFusion.Application;
using MangaFusion.Application.Realtime;
using MangaFusion.Application.Settings;
using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure;
using MangaFusion.Infrastructure.Downloads;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Monitoring;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Settings;
using MangaFusion.Sources.ComicVine;
using MangaFusion.Sources.MangaDex;
using MangaFusion.Sources.MangaUpdates;
using MangaFusion.Sources.Web;
using MangaFusion.Web;
using MangaFusion.Web.Endpoints;
using MangaFusion.Web.Realtime;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// --- Dynamic log level: EF Core SQL/HttpClient/Hangfire logging stays quiet (Warning) by default;
// an admin can open everything up (Trace/Debug/...) at runtime via DynamicLogLevelService, which
// updates this same instance. Must be constructed and wired into the logging pipeline before the
// builder is built — the filter delegates close over it, so this is the only object that matters at
// runtime (a plain IConfiguration mutation was tried and doesn't reliably re-trigger MEL's filters).
var logLevelOverride = new LogLevelOverride();
builder.Services.AddSingleton(logLevelOverride);
foreach (var (category, baseline) in LogLevelOverride.ManagedCategories)
{
    builder.Logging.AddFilter(category, level => logLevelOverride.IsEnabled(baseline, level));
}

// --- DI container: Autofac hosts the app modules; framework services stay on IServiceCollection.
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// --- Persistence + Identity ------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole(Roles.Admin));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SourceExceptionHandler>();

// Used by the source cover proxy to fetch images server-side. Needs a User-Agent — some source
// CDNs (MangaDex) reject requests without one.
builder.Services.AddHttpClient(SourceEndpoints.CoverProxyClient, client =>
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "MangaFusion/0.1"));

// Cover/image fetches for the library (cover caching) — same User-Agent requirement.
builder.Services.AddHttpClient(MangaFusion.Infrastructure.Library.LibraryService.ImageClientName, client =>
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "MangaFusion/0.1"));

// Page-image fetches for the download engine (MangaDex@Home) — same User-Agent requirement. The
// preview reader's page proxy reuses this same named client.
builder.Services.AddHttpClient(DownloadOrchestrator.ImageClientName, client =>
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "MangaFusion/0.1"));

// Short-lived cache of source-resolved page sets, so the preview reader's manifest call and each
// per-page proxy call don't each re-hit the source's page-resolution API (MangaDex@Home tokens).
builder.Services.AddMemoryCache();

// --- Background jobs (Hangfire, persistent SQLite store) + realtime (SignalR) -----------------
var hangfireDb = builder.Configuration["Hangfire:ConnectionString"] ?? "data/hangfire.db";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(hangfireDb))!);
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    // Default InvisibilityTimeout (30 min) is too short for this app's longest-running jobs (PDF
    // rasterization/re-encoding across a big batch import or migration commit can easily exceed it) —
    // once exceeded, the storage assumes the worker died, cancels the job's token out from under it
    // mid-write, and a caught-and-logged cancellation gets silently reported as "Succeeded". A few
    // hours comfortably covers any realistic single job; a crashed worker's job just takes a bit
    // longer to become reclaimable, which is an acceptable trade-off for a single-worker deployment.
    .UseSQLiteStorage(hangfireDb, new SQLiteStorageOptions { InvisibilityTimeout = TimeSpan.FromHours(4) }));
builder.Services.AddHangfireServer();

builder.Services.AddSignalR();
builder.Services.AddSingleton<ILibraryNotifier, SignalRLibraryNotifier>();

// Persist Data Protection keys so auth cookies and (later) encrypted source credentials
// survive restarts / container recreation.
var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? "keys";
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("MangaFusion");

builder.Services.AddHealthChecks();

// --- Autofac module registration (per-assembly composition seams) ----------------------------
builder.Host.ConfigureContainer<ContainerBuilder>(container =>
{
    container.RegisterModule(new ApplicationModule());
    container.RegisterModule(new InfrastructureModule());
    container.RegisterModule(new MangaDexModule());
    container.RegisterModule(new MangaUpdatesModule());
    container.RegisterModule(new ComicVineModule());
    container.RegisterModule(new WebSourceModule());
});

var app = builder.Build();

// --- Apply migrations + seed roles/admin on startup ------------------------------------------
await DbInitializer.InitializeAsync(app.Services);

// Recurring monitoring scan (cadence from settings: DB override → config → default). Use the
// DI-based manager, not the static API.
using (var scope = app.Services.CreateScope())
{
    // Re-applies any admin-chosen minimum log level (or the quiet baseline) so a restart doesn't
    // silently drop the override.
    await scope.ServiceProvider.GetRequiredService<DynamicLogLevelService>().InitializeAsync(CancellationToken.None);

    // Force the path helpers to construct, which is what creates the directory tree (library roots +
    // ingest inboxes, each split into manga/ and comics/). They're otherwise resolved lazily on first use,
    // so a fresh install would show an empty volume with nowhere obvious to drop files.
    scope.ServiceProvider.GetRequiredService<LibraryPaths>();
    scope.ServiceProvider.GetRequiredService<LocalPaths>();
    scope.ServiceProvider.GetRequiredService<ImportPaths>();
    scope.ServiceProvider.GetRequiredService<MigrationPaths>();

    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
    var cron = await settings.GetMonitorCronAsync(CancellationToken.None);
    scope.ServiceProvider.GetRequiredService<IRecurringJobManager>().AddOrUpdate<MonitorScanJob>(
        "monitor-scan",
        monitor => monitor.ScanAllAsync(CancellationToken.None),
        cron);

    // Idempotent — safe to enqueue on every boot. Keeps the filter dropdowns and the local-import tag
    // picker populated from each source's own tag registry even before any series from it has been
    // imported (tag discovery would otherwise only happen lazily, per imported series). Sources with no
    // registry to publish (ComicVine has no global tag list) return an empty set and are a no-op.
    var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
    foreach (var source in scope.ServiceProvider.GetRequiredService<ISourceRegistry>().All)
    {
        if (source.Capabilities.HasFlag(SourceCapabilities.Metadata))
        {
            var sourceId = source.Id;
            jobs.Enqueue<LibraryService>(library => library.SyncSourceTagsAsync(sourceId, CancellationToken.None));
        }
    }
}

// --- Middleware ------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Gate self-registration behind a runtime setting. An authenticated admin can always create users
// (via /api/admin/users); anonymous self-registration is allowed only when the setting is on.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals("/api/auth/register", StringComparison.OrdinalIgnoreCase) &&
        !context.User.IsInRole(Roles.Admin))
    {
        var settings = context.RequestServices.GetRequiredService<ISettingsService>();
        if (!await settings.GetAllowSelfRegistrationAsync(context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Self-registration is disabled." });
            return;
        }
    }

    await next();
});

// --- Endpoints -------------------------------------------------------------------------------
app.MapHealthChecks("/health");

// Identity endpoints: /api/auth/register, /api/auth/login (?useCookies=true), /api/auth/refresh, ...
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();

// MapIdentityApi has no logout; clear the auth cookie explicitly.
app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.Ok();
    })
    .RequireAuthorization();

// Known UI theme ids — kept in sync with frontend/src/lib/theme.svelte.ts's THEMES list.
var knownThemeIds = new[] { "violet", "seal-ink", "jade", "momiji" };

// Minimal "who am I" endpoint to back the SPA session state. Includes the DB-persisted
// theme preference (a small extra read on an otherwise claims-only endpoint), since the
// SPA needs it as soon as the session loads.
app.MapGet("/api/me", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var dbUser = id is null ? null : await db.Users.FindAsync([Guid.Parse(id)], ct);
        return Results.Ok(new
        {
            id,
            email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name,
            roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            theme = dbUser?.Theme,
            defaultLanguage = dbUser?.DefaultLanguage,
            // Which library the SPA should open on. Rides the same session read as theme, so the mode
            // is known before the first page renders and no extra round-trip is needed.
            preferredKind = dbUser?.PreferredKind?.ToString().ToLowerInvariant(),
            homeAcrossLibraries = dbUser?.HomeAcrossLibraries ?? false
        });
    })
    .RequireAuthorization();

app.MapPut("/api/me/home-scope", async (
        HomeScopeRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
    {
        var id = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var dbUser = await db.Users.FindAsync([id], ct);
        if (dbUser is null) return Results.Unauthorized();

        dbUser.HomeAcrossLibraries = request.AcrossLibraries;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    })
    .RequireAuthorization();

app.MapPut("/api/me/mode", async (ModeRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
    {
        if (!Enum.TryParse<MangaFusion.Domain.Library.MediaKind>(request.Mode, ignoreCase: true, out var kind))
            return Results.BadRequest($"Unknown mode '{request.Mode}'.");

        var id = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var dbUser = await db.Users.FindAsync([id], ct);
        if (dbUser is null) return Results.Unauthorized();

        dbUser.PreferredKind = kind;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    })
    .RequireAuthorization();

app.MapPut("/api/me/theme", async (ThemeRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
    {
        if (!knownThemeIds.Contains(request.Theme))
            return Results.BadRequest($"Unknown theme '{request.Theme}'.");

        var id = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var dbUser = await db.Users.FindAsync([id], ct);
        if (dbUser is null) return Results.Unauthorized();

        dbUser.Theme = request.Theme;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    })
    .RequireAuthorization();

app.MapPut("/api/me/language", async (LanguageRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
    {
        var trimmed = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language.Trim();
        if (trimmed is not null && !MangaLanguage.IsKnown(trimmed))
            return Results.BadRequest($"Unknown language '{trimmed}'.");

        var id = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var dbUser = await db.Users.FindAsync([id], ct);
        if (dbUser is null) return Results.Unauthorized();

        dbUser.DefaultLanguage = trimmed;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    })
    .RequireAuthorization();

// Backs every language picker in the frontend (default language, follow/series auto-download
// languages) — a fixed known-language list rather than deriving options from whatever's already
// been observed/downloaded, so a language can be pre-selected before any release exists in it.
app.MapGet("/api/languages", () =>
        Results.Ok(MangaLanguage.KnownLanguages
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new { code = l.Code, name = l.Name })))
    .RequireAuthorization();

app.MapSourceEndpoints();
app.MapLibraryEndpoints();
app.MapReaderEndpoints();
app.MapMonitoringEndpoints();
app.MapAdminEndpoints();
app.MapAdminTaskEndpoints();
app.MapLocalEndpoints();
app.MapMigrationEndpoints();
app.MapImportEndpoints();

app.MapHub<LibraryHub>("/hubs/library");

// Hangfire dashboard — admin only.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new AdminDashboardAuthFilter()],
});

// SPA fallback: any non-API, non-file route serves the Svelte app shell.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;

public record ThemeRequest(string Theme);
public record LanguageRequest(string? Language);
public record ModeRequest(string Mode);
public record HomeScopeRequest(bool AcrossLibraries);
