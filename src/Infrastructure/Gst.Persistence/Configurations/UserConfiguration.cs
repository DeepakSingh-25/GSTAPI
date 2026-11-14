using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gst.Persistence.Configurations
{
    // Table design yahan, entity class saaf rehti hai
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("tb_users");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.FirstName).HasMaxLength(50).IsRequired().HasDefaultValue(string.Empty);
            builder.Property(x => x.LastName).HasMaxLength(50).IsRequired().HasDefaultValue(string.Empty);
            builder.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
            builder.HasIndex(x => x.Email)
                .IsUnique()
                .HasDatabaseName(DbIndexes.UserEmail)
                .HasFilter("[IsDeleted] = 0");
            builder.Property(x => x.MobileNo).HasMaxLength(10);
            builder.HasIndex(x => x.MobileNo)
                .IsUnique()
                .HasDatabaseName(DbIndexes.UserMobile)
                .HasFilter("[MobileNo] IS NOT NULL AND [IsDeleted] = 0");
            builder.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Role).HasMaxLength(50).IsRequired();
            builder.Property(x => x.EmailVerified).HasDefaultValue(false);
            builder.Property(x => x.OnboardingCompleted).HasDefaultValue(false);
            builder.Property(x => x.JobRole).HasMaxLength(50);
            builder.Property(x => x.FailedLoginCount).HasDefaultValue(0);

            // Legacy users ki company null; Restrict = company delete par users na hatein
            builder.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
