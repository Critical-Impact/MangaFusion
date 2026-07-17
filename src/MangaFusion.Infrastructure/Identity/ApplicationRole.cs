using Microsoft.AspNetCore.Identity;

namespace MangaFusion.Infrastructure.Identity;

/// <summary>Application role with a GUID key.</summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string name) : base(name)
    {
    }
}
