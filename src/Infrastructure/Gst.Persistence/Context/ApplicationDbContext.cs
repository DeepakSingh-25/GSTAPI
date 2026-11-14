using Gst.Application.Common.Exceptions;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Configurations;
using Gst.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;

namespace Gst.Persistence.Context
{
    public class ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IOptions<ConnectionStrings> connectionStrings,
        ICurrentUserService currentUser)
        : DbContext(options), IUnitOfWork
    {
        private readonly ConnectionStrings _connectionStrings = connectionStrings.Value;

        public DbSet<User> Users => Set<User>();
        public DbSet<Company> Companies => Set<Company>();
        public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            if (!string.IsNullOrWhiteSpace(_connectionStrings.DefaultSchema))
                builder.HasDefaultSchema(_connectionStrings.DefaultSchema);

            // Configurations/ folder ki saari IEntityTypeConfiguration apne aap lagti hain
            builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            // Soft-deleted rows har query se apne aap hat jaati hain
            foreach (var entityType in builder.Model.GetEntityTypes()
                         .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType)))
            {
                var param = Expression.Parameter(entityType.ClrType, "e");
                var body = Expression.Equal(Expression.Property(param, nameof(BaseEntity.IsDeleted)), Expression.Constant(false));
                builder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, param));
            }
        }

        // Audit columns (CreatedBy / ModifiedBy) apne aap bharte hain
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var who = currentUser.Email ?? "system";
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedOn = DateTime.UtcNow;
                    entry.Entity.CreatedBy ??= who;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.ModifiedOn = DateTime.UtcNow;
                    entry.Entity.ModifiedBy = who;
                }
            }

            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            // Unique index toota (2601 / 2627) -> DuplicateKeyException, handler 409 banata hai
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
            {
                throw new DuplicateKeyException(SqlExceptionParser.GetIndexName(sql.Number, sql.Message), ex);
            }
        }
    }
}
