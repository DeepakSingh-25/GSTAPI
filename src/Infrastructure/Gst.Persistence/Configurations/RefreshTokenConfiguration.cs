using Gst.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gst.Persistence.Configurations
{
    public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.ToTable("tb_refresh_tokens");
            builder.HasKey(x => x.Id);

            // SHA256 hex = 64 chars
            builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            builder.Property(x => x.CreatedByIp).HasMaxLength(45);

            // User navigation optional — soft-delete query filter ka warning na aaye
            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.TokenHash).IsUnique();
            // logout-all / cleanup ke liye
            builder.HasIndex(x => new { x.UserId, x.RevokedOn });
        }
    }
}
