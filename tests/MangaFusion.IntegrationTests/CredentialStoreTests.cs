using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.IntegrationTests;

public class CredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mf-cred-{Guid.NewGuid():N}");
    private readonly IDataProtectionProvider _dp;

    public CredentialStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_dir, "keys")));
    }

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "test.db")}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Set_then_Get_roundtrips_and_stores_ciphertext()
    {
        await using (var ctx = NewContext())
        {
            await ctx.Database.MigrateAsync();
        }

        var values = new Dictionary<string, string> { ["clientId"] = "cid", ["password"] = "s3cret" };

        await using (var ctx = NewContext())
        {
            await new DataProtectionCredentialStore(ctx, _dp).SetAsync("mangadex", values);
        }

        // Payload on disk must not contain the plaintext secret.
        await using (var ctx = NewContext())
        {
            var row = await ctx.SourceCredentials.SingleAsync();
            Assert.DoesNotContain("s3cret", row.EncryptedPayload);
        }

        await using (var ctx = NewContext())
        {
            var store = new DataProtectionCredentialStore(ctx, _dp);
            Assert.True(await store.ExistsAsync("mangadex"));

            var read = await store.GetAsync("mangadex");
            Assert.NotNull(read);
            Assert.Equal("cid", read!["clientId"]);
            Assert.Equal("s3cret", read["password"]);

            await store.DeleteAsync("mangadex");
            Assert.False(await store.ExistsAsync("mangadex"));
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
