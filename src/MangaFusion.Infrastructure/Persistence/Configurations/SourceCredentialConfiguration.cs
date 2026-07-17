using MangaFusion.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MangaFusion.Infrastructure.Persistence.Configurations;

public class SourceCredentialConfiguration : IEntityTypeConfiguration<SourceCredential>
{
    public void Configure(EntityTypeBuilder<SourceCredential> builder)
    {
        builder.ToTable("SourceCredentials");
        builder.HasKey(x => x.SourceId);
        builder.Property(x => x.SourceId).HasMaxLength(64);
        builder.Property(x => x.EncryptedPayload).IsRequired();
    }
}
