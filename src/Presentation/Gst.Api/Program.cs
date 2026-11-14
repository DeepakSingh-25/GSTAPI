using Gst.Api.Extensions;
using Gst.Api.Middleware;
using Gst.Api.Services;
using Gst.Application;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using Gst.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Logging (appsettings.json -> "Serilog" section)
    builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration));

    // Layers
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddApplicationServices();
    builder.Services.AddPersistenceServices(builder.Configuration);
    builder.Services.AddScoped<IAuthSessionService, AuthSessionService>();

    // Api
    builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
        .ConfigureApiBehaviorOptions(o =>
        {
            // Model binding fail (bad JSON) bhi ApiResponse format me 400
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid request." : e.ErrorMessage);
                return new BadRequestObjectResult(
                    ApiResponse<string>.Fail(string.Join(", ", errors), HttpStatusCode.BadRequest));
            };
        });
    builder.Services.AddForwardedHeadersSupport(builder.Configuration);
    builder.Services.AddRateLimiting();
    builder.Services.AddCorsPolicy(builder.Configuration);
    builder.Services.AddJwtAuthentication(builder.Configuration);
    builder.Services.AddSwaggerDocs();

    var app = builder.Build();

    // Sabse pehle: rate limiter aur RefreshToken.CreatedByIp ko asli client IP mile
    app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        app.UseHsts();
    }

    // Request log pehle: exception middleware andar hai, to log tak raw exception text (PII) nahi pahunchta
    app.UseSerilogRequestLogging();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseHttpsRedirection();
    app.UseCors(ServiceExtensions.CorsPolicy);
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Gst.Api failed to start");
}
finally
{
    Log.CloseAndFlush();
}

// WebApplicationFactory (tests) ke liye
public partial class Program;
