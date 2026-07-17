using MangaFusion.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations and seeds baseline data (roles + an initial admin user)
/// on startup. Idempotent: safe to run on every boot.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer).FullName!);

        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
                logger.LogInformation("Created role {Role}", role);
            }
        }

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if (!await userManager.Users.AnyAsync(ct))
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var email = config["Seed:AdminEmail"] ?? "admin@mangafusion.local";
            var password = config["Seed:AdminPassword"] ?? "ChangeMe!123";

            var admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, Roles.Admin);
                logger.LogWarning("Seeded initial admin user {Email}. Change the password after first login.", email);
            }
            else
            {
                logger.LogError("Failed to seed admin user: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
