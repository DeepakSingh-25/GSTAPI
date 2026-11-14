using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gst.Persistence.Configurations
{
    public class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
    {
        public void Configure(EntityTypeBuilder<OtpCode> builder)
        {
            builder.ToTable("tb_otp_codes");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
            builder.Property(x => x.Purpose).HasMaxLength(20).IsRequired();
            // SHA256 hex = 64 chars, ASCII
            builder.Property(x => x.CodeHash).HasMaxLength(64).IsUnicode(false).IsRequired();

            // User navigation optional — soft-delete query filter ka warning na aaye
            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Resend limit check ke liye
            builder.HasIndex(x => new { x.Email, x.Purpose, x.CreatedOn });

            // FK index alag se rakho — neeche wala filtered index consumed rows cover nahi karta
            builder.HasIndex(x => x.UserId);

            // Ek user + purpose ka ek hi chalu (unconsumed) OTP — parallel resend / login se N OTP na banein
            builder.HasIndex(x => new { x.UserId, x.Purpose })
                .IsUnique()
                .HasFilter("[ConsumedOn] IS NULL")
                .HasDatabaseName(DbIndexes.OtpUnconsumed);
        }
    }
}
