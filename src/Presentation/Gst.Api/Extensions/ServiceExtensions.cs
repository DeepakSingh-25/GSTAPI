using Gst.Domain.Common;
using Gst.Domain.Configurations;
using Gst.Domain.Constants;
using Gst.Persistence;
using Gst.Api.Filters;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

namespace Gst.Api.Extensions
{
    public static class ServiceExtensions
    {
        public const string CorsPolicy = "CorsPolicy";

        public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
        {
            var origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
            services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
                p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
            return services;
        }

        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var jwt = configuration.GetSection("Jwt").Get<JwtConfig>() ?? new JwtConfig();
            // Config seedha padhte hain (options validation se pehle), isliye yahan bhi fail fast
            if (!jwt.HasStrongKey())
                throw new InvalidOperationException(PersistenceServiceRegistration.JwtKeyError);

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // Claims ke short names ("sub", "role") jaise hain waise hi rahein
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = AppClaims.Name,
                        RoleClaimType = AppClaims.Role,
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwt.Issuer,
                        ValidAudience = jwt.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                        ClockSkew = TimeSpan.Zero
                    };

                    // 401 / 403 bhi ApiResponse format me
                    options.Events = new JwtBearerEvents
                    {
                        // Authorization header nahi hai to cookie se token (header path Swagger ke liye rehta hai)
                        OnMessageReceived = ctx =>
                        {
                            if (!ctx.Request.Headers.ContainsKey("Authorization")
                                && ctx.Request.Cookies.TryGetValue(AuthCookies.Access, out var token))
                                ctx.Token = token;
                            return Task.CompletedTask;
                        },
                        OnChallenge = async ctx =>
                        {
                            ctx.HandleResponse();
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            await ctx.Response.WriteAsJsonAsync(ApiResponse<string>.Fail("Login required or token invalid.", HttpStatusCode.Unauthorized));
                        },
                        OnForbidden = ctx =>
                        {
                            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return ctx.Response.WriteAsJsonAsync(ApiResponse<string>.Fail("You do not have permission for this action.", HttpStatusCode.Forbidden));
                        }
                    };
                });
            // Policies — naya rule yahan add karo, controller par [Authorize(Policy = ...)] lagao
            services.AddAuthorizationBuilder()
                .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Owner));
            return services;
        }

        // IIS in-process client IP khud deta hai. Agar beech me proxy (nginx/LB) ho to uska IP
        // "ForwardedHeaders:KnownProxies" me daalo. Default: sirf loopback trusted.
        public static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services, IConfiguration configuration)
        {
            var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                foreach (var proxy in proxies)
                    o.KnownProxies.Add(IPAddress.Parse(proxy));
            });
            return services;
        }

        // Rate limit — "auth" policy: IP ke hisaab se 10 request / minute
        public static IServiceCollection AddRateLimiting(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (ctx, token) =>
                {
                    ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await ctx.HttpContext.Response.WriteAsJsonAsync(
                        ApiResponse<string>.Fail("Too many requests. Please try again later.", HttpStatusCode.TooManyRequests), token);
                };
                options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ClientPartitionKey(httpContext.Connection.RemoteIpAddress),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
                // "refresh" policy: IP ke hisaab se 30 request / minute
                options.AddPolicy(RateLimitPolicies.Refresh, httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ClientPartitionKey(httpContext.Connection.RemoteIpAddress),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            });

            // Dusra limiter: otpId / login userId pe (AuthKeyRateLimitFilter, Auth:KeyRateLimit* config)
            services.AddSingleton<AuthKeyRateLimiter>();
            services.AddScoped<AuthKeyRateLimitFilter>();
            return services;
        }

        // IP limiter ki partition key. IPv6 /64 tak group (ek ghar / VM ko poora /64 milta hai, warna har address naya bucket);
        // IPv4-mapped IPv6 ko IPv4 maano.
        public static string ClientPartitionKey(IPAddress? ip)
        {
            if (ip is null)
                return "unknown";
            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();
            if (ip.AddressFamily != AddressFamily.InterNetworkV6)
                return ip.ToString();

            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return $"{new IPAddress(bytes)}/64";
        }

        public static IServiceCollection AddSwaggerDocs(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Gst API", Version = "v1" });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT token daalo"
                });
                c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", doc)] = []
                });
            });
            return services;
        }
    }
}
