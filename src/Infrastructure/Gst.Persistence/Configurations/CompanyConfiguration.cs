using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gst.Persistence.Configurations
{
    public class CompanyConfiguration : IEntityTypeConfiguration<Company>
    {
        public void Configure(EntityTypeBuilder<Company> builder)
        {
            builder.ToTable("tb_companies");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.LegalName).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Gstin).HasMaxLength(15).IsRequired();
            builder.Property(x => x.Pan).HasMaxLength(10).IsRequired();
            builder.Property(x => x.RegistrationType).HasMaxLength(20).IsRequired();
            builder.Property(x => x.State).HasMaxLength(50).IsRequired();
            builder.Property(x => x.StateCode).HasMaxLength(2).IsRequired();

            // GSTIN unique — sirf active (non-deleted) rows par
            builder.HasIndex(x => x.Gstin)
                .IsUnique()
                .HasDatabaseName(DbIndexes.CompanyGstin)
                .HasFilter("[IsDeleted] = 0");

            // Optional columns
            builder.Property(x => x.TradeName).HasMaxLength(100);
            builder.Property(x => x.BusinessSize).HasMaxLength(20);
            builder.Property(x => x.Industry).HasMaxLength(100);
            builder.Property(x => x.AddressLine1).HasMaxLength(200);
            builder.Property(x => x.AddressLine2).HasMaxLength(200);
            builder.Property(x => x.City).HasMaxLength(100);
            builder.Property(x => x.Pincode).HasMaxLength(6);
            builder.Property(x => x.Email).HasMaxLength(256);
            builder.Property(x => x.Phone).HasMaxLength(15);
            builder.Property(x => x.Website).HasMaxLength(200);
            builder.Property(x => x.BankName).HasMaxLength(100);
            builder.Property(x => x.AccountNo).HasMaxLength(20);
            builder.Property(x => x.Ifsc).HasMaxLength(11);
            builder.Property(x => x.Branch).HasMaxLength(100);
            builder.Property(x => x.TermsAndConditions).HasMaxLength(2000);
        }
    }
}
