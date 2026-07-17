namespace MangaFusion.Infrastructure.Identity;

/// <summary>Canonical role names used for authorization across the application.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static readonly string[] All = [Admin, User];
}
