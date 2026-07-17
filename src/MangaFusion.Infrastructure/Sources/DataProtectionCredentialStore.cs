using System.Text.Json;
using MangaFusion.Contracts.Sources;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Sources;

/// <summary>Persists source credentials as a Data-Protection-encrypted JSON dictionary in the DB.
/// Nothing here logs credential values.</summary>
public sealed class DataProtectionCredentialStore : ISourceCredentialStore
{
    private const string Purpose = "source-credentials";

    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;

    public DataProtectionCredentialStore(AppDbContext db, IDataProtectionProvider provider)
    {
        _db = db;
        _protector = provider.CreateProtector(Purpose);
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetAsync(string sourceId, CancellationToken ct = default)
    {
        var row = await _db.SourceCredentials.FindAsync(new object?[] { sourceId }, ct);
        if (row is null)
        {
            return null;
        }

        var json = _protector.Unprotect(row.EncryptedPayload);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }

    public async Task SetAsync(
        string sourceId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        var payload = _protector.Protect(JsonSerializer.Serialize(values));

        var row = await _db.SourceCredentials.FindAsync(new object?[] { sourceId }, ct);
        if (row is null)
        {
            _db.SourceCredentials.Add(new SourceCredential
            {
                SourceId = sourceId,
                EncryptedPayload = payload,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.EncryptedPayload = payload;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsAsync(string sourceId, CancellationToken ct = default) =>
        _db.SourceCredentials.AnyAsync(x => x.SourceId == sourceId, ct);

    public async Task DeleteAsync(string sourceId, CancellationToken ct = default)
    {
        var row = await _db.SourceCredentials.FindAsync(new object?[] { sourceId }, ct);
        if (row is not null)
        {
            _db.SourceCredentials.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
    }
}
