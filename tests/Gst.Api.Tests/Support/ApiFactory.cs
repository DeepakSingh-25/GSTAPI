using Gst.Application.Interfaces.IService;
using Gst.Persistence.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gst.Api.Tests.Support;

// SQLite in-memory + fake email — shared SQL Server (192.168.120.17) ko kabhi nahi chhuta
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string TestJwtKey = "gst-tests-only-signing-key-0123456789abcdef";

    private readonly SqliteConnection connection = new("DataSource=:memory:");

    internal FakeEmailSender Email { get; } = new();

    private readonly Dictionary<string, string?> extraSettings = [];

    public ApiFactory()
    {
        connection.Open();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
    }

    // Extra config (e.g. "Auth:KeyRateLimitPermits") — internal taaki xUnit fixture ka ek hi public ctor rahe
    internal ApiFactory(Dictionary<string, string?> settings)
    {
        extraSettings = settings;
        connection.Open();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Startup placeholder key reject karta hai — test-only strong key (32+ bytes).
        // UseSetting chahiye: Program.cs Build() se pehle config padhta hai, ConfigureAppConfiguration baad me lagta hai
        builder.UseSetting("Jwt:Key", TestJwtKey);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Schema khali (SQLite me schema nahi), connection string bhi dummy
            ["ConnectionStrings:DefaultSchema"] = "",
            ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
            ["Smtp:Host"] = "",
            ["Jwt:Key"] = TestJwtKey
        }).AddInMemoryCollection(extraSettings));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<ApplicationDbContext>));
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    public ApplicationDbContext OpenDb() =>
        Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}
