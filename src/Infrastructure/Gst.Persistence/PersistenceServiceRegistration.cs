using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Configurations;
using Gst.Persistence.Context;
using Gst.Persistence.Repository;
using Gst.Persistence.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gst.Persistence
{
    public static class PersistenceServiceRegistration
    {
        public const string JwtKeyError =
            "Jwt:Key is a placeholder or shorter than 32 bytes. Set a real key via user-secrets or the Jwt__Key env var.";

        public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configurations
            services.Configure<ConnectionStrings>(configuration.GetSection("ConnectionStrings"));
            services.AddOptions<JwtConfig>()
                .Bind(configuration.GetSection("Jwt"))
                .Validate(k => k.HasStrongKey(), JwtKeyError)
                .ValidateOnStart();
            services.Configure<SmtpConfig>(configuration.GetSection("Smtp"));
            services.Configure<AuthConfig>(configuration.GetSection("Auth"));

            // DB Context — EF Core (write) + Dapper (fast read)
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"),
                    sql => sql.EnableRetryOnFailure(maxRetryCount: 3));
                // Duplicate-key exception text (PII) EF Error level par log na kare
                options.ConfigureWarnings(w => w
                    .Log((CoreEventId.SaveChangesFailed, LogLevel.Debug))
                    .Log((RelationalEventId.CommandError, LogLevel.Debug)));
            });
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<IDapperDbContext, DapperDbContext>();

            // Services
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<IEmailSender, SmtpEmailSender>();

            // Repositories — naya repository yahan register karo
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ICompanyRepository, CompanyRepository>();
            services.AddScoped<IOtpCodeRepository, OtpCodeRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

            return services;
        }
    }
}
