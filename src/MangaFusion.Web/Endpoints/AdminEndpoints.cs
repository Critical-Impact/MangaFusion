using System.Security.Claims;
using Hangfire;
using MangaFusion.Application.Settings;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Monitoring;
using MangaFusion.Infrastructure.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Web.Endpoints;

/// <summary>Admin-only management: global settings + user administration. Settings reads return
/// effective (DB-over-config-over-default) values; writes persist a DB override (a null/empty field
/// clears it). Changing the cron re-registers the recurring monitor scan.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        group.MapGet("/settings", GetSettings);
        group.MapPut("/settings", PutSettings);

        group.MapGet("/users", ListUsers);
        group.MapPost("/users", CreateUser);
        group.MapPost("/users/{id:guid}/roles", SetRoles);
        group.MapPost("/users/{id:guid}/disable", DisableUser);
        group.MapPost("/users/{id:guid}/enable", EnableUser);
        group.MapDelete("/users/{id:guid}", DeleteUser);
    }

    private static async Task<IResult> GetSettings(ISettingsService settings, CancellationToken ct) =>
        Results.Ok(await ToDto(settings, ct));

    private static async Task<IResult> PutSettings(
        UpdateSettingsRequest request,
        ISettingsService settings,
        IRecurringJobManager recurring,
        DynamicLogLevelService logLevels,
        CancellationToken ct)
    {
        if (request.MonitorCron is not null)
        {
            var cron = request.MonitorCron.Trim();
            if (cron.Length == 0)
            {
                return Results.BadRequest(new { error = "Cron expression cannot be empty." });
            }

            try
            {
                // Registering validates the expression (Hangfire throws on a bad one) and applies it.
                recurring.AddOrUpdate<MonitorScanJob>(
                    "monitor-scan", m => m.ScanAllAsync(CancellationToken.None), cron);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Invalid cron expression: {ex.Message}" });
            }

            await settings.SetAsync(SettingKeys.MonitorCron, cron, ct);
        }

        if (request.DefaultLanguages is not null)
        {
            var csv = string.Join(',', request.DefaultLanguages.Select(l => l.Trim()).Where(l => l.Length > 0));
            await settings.SetAsync(SettingKeys.DefaultLanguages, csv.Length == 0 ? null : csv, ct);
        }

        if (request.DefaultGraceDays is not null)
        {
            if (request.DefaultGraceDays < 0)
            {
                return Results.BadRequest(new { error = "Grace period days must be zero or greater." });
            }

            await settings.SetAsync(SettingKeys.DefaultGraceDays, request.DefaultGraceDays.Value.ToString(), ct);
        }

        if (request.AllowSelfRegistration is not null)
        {
            await settings.SetAsync(
                SettingKeys.AllowSelfRegistration, request.AllowSelfRegistration.Value ? "true" : "false", ct);
        }

        if (request.MinimumLogLevel is not null)
        {
            try
            {
                // Blank clears the override, reverting to the quiet baseline.
                await logLevels.ApplyAsync(request.MinimumLogLevel.Trim(), ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }

        return Results.Ok(await ToDto(settings, ct));
    }

    private static async Task<SettingsDto> ToDto(ISettingsService settings, CancellationToken ct)
    {
        var e = await settings.GetEffectiveAsync(ct);
        return new SettingsDto(
            e.MonitorCron, e.DefaultLanguages, e.DefaultGraceDays, e.AllowSelfRegistration, e.MinimumLogLevel);
    }

    // --- User administration ---------------------------------------------------------------------

    private static async Task<IResult> ListUsers(UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var all = await users.Users.ToListAsync(ct);
        var dtos = new List<AdminUserDto>(all.Count);
        foreach (var user in all)
        {
            var roles = await users.GetRolesAsync(user);
            dtos.Add(new AdminUserDto(user.Id, user.Email, [.. roles], IsDisabled(user)));
        }

        return Results.Ok(dtos.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "Email and password are required." });
        }

        var roles = NormalizeRoles(request.Roles);
        if (roles is null)
        {
            return Results.BadRequest(new { error = "Unknown role requested." });
        }

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return Results.BadRequest(new { error = string.Join(' ', created.Errors.Select(e => e.Description)) });
        }

        if (roles.Count > 0)
        {
            await users.AddToRolesAsync(user, roles);
        }

        return Results.Ok(new AdminUserDto(user.Id, user.Email, [.. roles], IsDisabled(user)));
    }

    private static async Task<IResult> SetRoles(
        Guid id, SetRolesRequest request, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        var desired = NormalizeRoles(request.Roles);
        if (desired is null)
        {
            return Results.BadRequest(new { error = "Unknown role requested." });
        }

        var current = await users.GetRolesAsync(user);

        // Guard: don't strip the last admin of the Admin role.
        if (current.Contains(Roles.Admin) && !desired.Contains(Roles.Admin) && await IsLastAdmin(users, user))
        {
            return Results.BadRequest(new { error = "Cannot remove the Admin role from the last administrator." });
        }

        await users.RemoveFromRolesAsync(user, current.Except(desired));
        await users.AddToRolesAsync(user, desired.Except(current));

        return Results.Ok(new AdminUserDto(user.Id, user.Email, [.. desired], IsDisabled(user)));
    }

    private static async Task<IResult> DisableUser(
        Guid id, ClaimsPrincipal caller, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Id == CurrentUser(caller))
        {
            return Results.BadRequest(new { error = "You cannot disable your own account." });
        }

        if (await users.IsInRoleAsync(user, Roles.Admin) && await IsLastAdmin(users, user))
        {
            return Results.BadRequest(new { error = "Cannot disable the last administrator." });
        }

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        return Results.NoContent();
    }

    private static async Task<IResult> EnableUser(Guid id, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        await users.SetLockoutEndDateAsync(user, null);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid id, ClaimsPrincipal caller, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Id == CurrentUser(caller))
        {
            return Results.BadRequest(new { error = "You cannot delete your own account." });
        }

        if (await users.IsInRoleAsync(user, Roles.Admin) && await IsLastAdmin(users, user))
        {
            return Results.BadRequest(new { error = "Cannot delete the last administrator." });
        }

        await users.DeleteAsync(user); // FKs cascade the user's progress/follows/notifications
        return Results.NoContent();
    }

    private static bool IsDisabled(ApplicationUser user) =>
        user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;

    private static async Task<bool> IsLastAdmin(UserManager<ApplicationUser> users, ApplicationUser candidate)
    {
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        return admins.Count(a => a.Id != candidate.Id) == 0;
    }

    /// <summary>Returns the requested roles restricted to the known set, or null if any is unknown.</summary>
    private static List<string>? NormalizeRoles(string[]? roles)
    {
        if (roles is null || roles.Length == 0)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var role in roles.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct())
        {
            var match = Roles.All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }

            result.Add(match);
        }

        return result;
    }

    private static Guid CurrentUser(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record SettingsDto(
    string MonitorCron, IReadOnlyList<string> DefaultLanguages, int DefaultGraceDays, bool AllowSelfRegistration,
    string? MinimumLogLevel);

public sealed record UpdateSettingsRequest(
    string? MonitorCron, string[]? DefaultLanguages, int? DefaultGraceDays, bool? AllowSelfRegistration,
    string? MinimumLogLevel);

public sealed record AdminUserDto(Guid Id, string? Email, IReadOnlyList<string> Roles, bool Disabled);

public sealed record CreateUserRequest(string Email, string Password, string[]? Roles);

public sealed record SetRolesRequest(string[] Roles);
