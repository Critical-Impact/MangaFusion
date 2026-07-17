using System.Net;
using System.Net.Http.Json;
using MangaFusion.Infrastructure.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace MangaFusion.IntegrationTests;

public class WebAppSmokeTests(MangaFusionAppFactory factory) : IClassFixture<MangaFusionAppFactory>
{
    [Fact]
    public async Task Health_returns_ok()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The full-library sweep is only ever constructed by Hangfire when the job actually fires, so a
    /// broken registration wouldn't surface at boot — it would surface as a silently failing scan, hours
    /// later. Resolve it here instead: it's a singleton that reaches into a per-series scope, which is
    /// exactly the wiring most likely to be got wrong.</summary>
    [Fact]
    public void Monitor_scan_job_resolves_from_the_container()
    {
        using var scope = factory.Services.CreateScope();

        var job = scope.ServiceProvider.GetRequiredService<MonitorScanJob>();
        Assert.NotNull(job);

        // And the per-series scope it opens must be able to produce the thing it fans out to.
        using var perSeries = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        Assert.NotNull(perSeries.ServiceProvider.GetRequiredService<MonitorService>());
    }

    [Fact]
    public async Task Sources_endpoint_requires_authentication()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/sources");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_login_then_me_succeeds()
    {
        var client = factory.CreateClient();
        var email = $"user{Guid.NewGuid():N}@test.local";
        var body = new { email, password = "Passw0rd!" };

        (await client.PostAsJsonAsync("/api/auth/register", body)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/login?useCookies=true", body)).EnsureSuccessStatusCode();

        var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Admin_settings_forbidden_for_normal_user_ok_for_admin()
    {
        // Seeded admin can read + update settings.
        var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email = "admin@mangafusion.local", password = "ChangeMe!123" })).EnsureSuccessStatusCode();

        var get = await admin.GetAsync("/api/admin/settings");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var updated = await admin.PutAsJsonAsync("/api/admin/settings",
            new { monitorCron = "*/30 * * * *", defaultLanguages = new[] { "en", "es" }, defaultGraceDays = 5 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.Equal("*/30 * * * *", body!.MonitorCron);
        Assert.Equal(5, body.DefaultGraceDays);

        // An invalid cron is rejected.
        var bad = await admin.PutAsJsonAsync("/api/admin/settings", new { monitorCron = "not a cron" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // A normal user is forbidden.
        var user = factory.CreateClient();
        var email = $"user{Guid.NewGuid():N}@test.local";
        var creds = new { email, password = "Passw0rd!" };
        (await user.PostAsJsonAsync("/api/auth/register", creds)).EnsureSuccessStatusCode();
        (await user.PostAsJsonAsync("/api/auth/login?useCookies=true", creds)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/settings")).StatusCode);
    }

    private sealed record SettingsResponse(string MonitorCron, string[] DefaultLanguages, int DefaultGraceDays);

    [Fact]
    public async Task Admin_tasks_feed_ok_for_admin_forbidden_for_user()
    {
        var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email = "admin@mangafusion.local", password = "ChangeMe!123" })).EnsureSuccessStatusCode();

        // Exercises the real Hangfire monitoring wrapper against the test SQLite storage.
        var resp = await admin.GetAsync("/api/admin/tasks");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var feed = await resp.Content.ReadFromJsonAsync<TaskFeedResponse>();
        Assert.NotNull(feed!.Stats);
        Assert.NotNull(feed.Items);

        var user = factory.CreateClient();
        var creds = new { email = $"user{Guid.NewGuid():N}@test.local", password = "Passw0rd!" };
        (await user.PostAsJsonAsync("/api/auth/register", creds)).EnsureSuccessStatusCode();
        (await user.PostAsJsonAsync("/api/auth/login?useCookies=true", creds)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/tasks")).StatusCode);
    }

    private sealed record TaskFeedResponse(object Stats, object[] Items);

    [Fact]
    public async Task Local_endpoints_admin_only_and_create_series_works()
    {
        var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email = "admin@mangafusion.local", password = "ChangeMe!123" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/local/inbox")).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/local/series",
            new { title = "My Local Book", contentRating = "Safe", status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var list = await admin.GetFromJsonAsync<LocalSeriesRow[]>("/api/local/series");
        Assert.Contains(list!, s => s.Title == "My Local Book");

        var user = factory.CreateClient();
        var creds = new { email = $"user{Guid.NewGuid():N}@test.local", password = "Passw0rd!" };
        (await user.PostAsJsonAsync("/api/auth/register", creds)).EnsureSuccessStatusCode();
        (await user.PostAsJsonAsync("/api/auth/login?useCookies=true", creds)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/local/inbox")).StatusCode);
    }

    private sealed record LocalSeriesRow(Guid Id, string Title);

    [Fact]
    public async Task Reader_endpoints_require_authentication()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/library/continue-reading");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reader_manifest_is_404_for_unknown_chapter_and_continue_reading_is_empty()
    {
        var client = factory.CreateClient();
        var email = $"user{Guid.NewGuid():N}@test.local";
        var body = new { email, password = "Passw0rd!" };
        (await client.PostAsJsonAsync("/api/auth/register", body)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/login?useCookies=true", body)).EnsureSuccessStatusCode();

        var manifest = await client.GetAsync($"/api/library/chapters/{Guid.NewGuid()}/manifest");
        Assert.Equal(HttpStatusCode.NotFound, manifest.StatusCode);

        var rail = await client.GetAsync("/api/library/continue-reading");
        Assert.Equal(HttpStatusCode.OK, rail.StatusCode);
        var items = await rail.Content.ReadFromJsonAsync<object[]>();
        Assert.Empty(items!);
    }
}
