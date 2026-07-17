using Hangfire.Dashboard;
using MangaFusion.Infrastructure.Identity;

namespace MangaFusion.Web.Realtime;

/// <summary>Restricts the Hangfire dashboard to authenticated admins (it uses the cookie auth context).</summary>
public sealed class AdminDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true && http.User.IsInRole(Roles.Admin);
    }
}
