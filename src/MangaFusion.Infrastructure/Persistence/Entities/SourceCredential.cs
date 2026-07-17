namespace MangaFusion.Infrastructure.Persistence.Entities;

/// <summary>Encrypted credentials for one source, keyed by source id. The payload is a
/// Data-Protection-encrypted JSON dictionary of field name -> value.</summary>
public class SourceCredential
{
    public string SourceId { get; set; } = default!;
    public string EncryptedPayload { get; set; } = default!;
    public DateTimeOffset UpdatedAt { get; set; }
}
