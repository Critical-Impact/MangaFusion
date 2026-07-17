using System.Net;
using System.Net.Http.Json;

namespace MangaFusion.IntegrationTests;

/// <summary>Drives the admin user-management API through the real host + seeded admin. Tests share
/// one DB (class fixture), so each keeps the admin invariant balanced: the seeded admin stays the
/// sole administrator (any promotion is demoted back).</summary>
public class AdminUserApiTests(MangaFusionAppFactory factory) : IClassFixture<MangaFusionAppFactory>
{
    private const string AdminEmail = "admin@mangafusion.local";
    private const string AdminPassword = "ChangeMe!123";

    private sealed record UserDto(Guid Id, string Email, string[] Roles, bool Disabled);

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email = AdminEmail, password = AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Admin_can_create_list_and_change_user_roles()
    {
        var admin = await AdminClientAsync();
        var email = $"member{Guid.NewGuid():N}@test.local";

        var createResp = await admin.PostAsJsonAsync("/api/admin/users",
            new { email, password = "Passw0rd!", roles = new[] { "User" } });
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<UserDto>();
        Assert.Contains("User", created!.Roles);

        var list = await admin.GetFromJsonAsync<UserDto[]>("/api/admin/users");
        Assert.Contains(list!, u => u.Email == email);

        // Promote to admin, then demote back (keeps the sole-admin invariant for other tests).
        var promote = await admin.PostAsJsonAsync($"/api/admin/users/{created.Id}/roles",
            new { roles = new[] { "Admin", "User" } });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        Assert.Contains("Admin", (await promote.Content.ReadFromJsonAsync<UserDto>())!.Roles);

        var demote = await admin.PostAsJsonAsync($"/api/admin/users/{created.Id}/roles",
            new { roles = new[] { "User" } });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
    }

    [Fact]
    public async Task Last_admin_is_protected()
    {
        var admin = await AdminClientAsync();
        var users = await admin.GetFromJsonAsync<UserDto[]>("/api/admin/users");
        var seeded = users!.Single(u => u.Email == AdminEmail);

        // Removing Admin from the only admin is rejected.
        var strip = await admin.PostAsJsonAsync($"/api/admin/users/{seeded.Id}/roles",
            new { roles = new[] { "User" } });
        Assert.Equal(HttpStatusCode.BadRequest, strip.StatusCode);

        // Deleting / disabling yourself (the last admin) is rejected.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.DeleteAsync($"/api/admin/users/{seeded.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/admin/users/{seeded.Id}/disable", new { })).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_blocks_login_until_re_enabled()
    {
        var admin = await AdminClientAsync();
        var email = $"lock{Guid.NewGuid():N}@test.local";
        var creds = new { email, password = "Passw0rd!" };

        var created = await (await admin.PostAsJsonAsync("/api/admin/users",
            new { email, password = "Passw0rd!", roles = new[] { "User" } })).Content.ReadFromJsonAsync<UserDto>();

        // Can log in initially.
        Assert.True((await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login?useCookies=true", creds)).IsSuccessStatusCode);

        // Disabled → login blocked.
        (await admin.PostAsJsonAsync($"/api/admin/users/{created!.Id}/disable", new { })).EnsureSuccessStatusCode();
        Assert.False((await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login?useCookies=true", creds)).IsSuccessStatusCode);

        // Re-enabled → login works again.
        (await admin.PostAsJsonAsync($"/api/admin/users/{created.Id}/enable", new { })).EnsureSuccessStatusCode();
        Assert.True((await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login?useCookies=true", creds)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Self_registration_gate_honors_the_setting()
    {
        var admin = await AdminClientAsync();

        // Turn self-registration off → anonymous register is forbidden.
        (await admin.PutAsJsonAsync("/api/admin/settings", new { allowSelfRegistration = false })).EnsureSuccessStatusCode();
        var blocked = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"x{Guid.NewGuid():N}@test.local", password = "Passw0rd!" });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        // Turn it back on (default) → register works again.
        (await admin.PutAsJsonAsync("/api/admin/settings", new { allowSelfRegistration = true })).EnsureSuccessStatusCode();
        var ok = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"y{Guid.NewGuid():N}@test.local", password = "Passw0rd!" });
        Assert.True(ok.IsSuccessStatusCode);
    }

    [Fact]
    public async Task User_management_is_forbidden_for_non_admins()
    {
        var user = factory.CreateClient();
        var creds = new { email = $"plain{Guid.NewGuid():N}@test.local", password = "Passw0rd!" };
        (await user.PostAsJsonAsync("/api/auth/register", creds)).EnsureSuccessStatusCode();
        (await user.PostAsJsonAsync("/api/auth/login?useCookies=true", creds)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PostAsJsonAsync("/api/admin/users", new { email = "z@z.z", password = "Passw0rd!" })).StatusCode);
    }
}
