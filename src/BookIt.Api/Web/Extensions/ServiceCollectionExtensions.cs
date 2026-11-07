using System.Text;
using System.Threading.RateLimiting;
using BookIt.Api.Application.Abstractions;
using BookIt.Api.Application.Auth;
using BookIt.Api.Application.Bookings;
using BookIt.Api.Application.Catalog;
using BookIt.Api.Application.Invoices;
using BookIt.Api.Domain;
using BookIt.Api.Infrastructure.Persistence;
using BookIt.Api.Infrastructure.Security;
using BookIt.Api.Web.Health;
using BookIt.Api.Web.Middleware;
using BookIt.Api.Web.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace BookIt.Api.Web.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Use-case services (no framework or database specifics).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IServiceCatalog, ServiceCatalog>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        return services;
    }

    /// <summary>EF Core / SQLite, password hashing, token issuing and validated options.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddDbContext<BookItDb>(o => o.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=bookit.db"));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<BookItDb>());
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<ITokenService, TokenService>();

        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => env.IsDevelopment() || !o.Key.StartsWith(JwtOptions.DevKeyPrefix, StringComparison.Ordinal),
                "Jwt:Key is the committed development-only key. Set a real secret via the Jwt__Key environment variable.")
            .ValidateOnStart();
        return services;
    }

    /// <summary>HTTP concerns: authn/authz, MVC, ProblemDetails, rate limiting, CORS, health checks, Swagger.</summary>
    public static IServiceCollection AddWebApi(this IServiceCollection services)
    {
        // Key material comes from validated IOptions<JwtOptions>, resolved lazily so config overrides apply.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwtAccessor) =>
            {
                var jwt = jwtAccessor.Value;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
        services.AddAuthorization();

        services.AddControllers();
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>((o, rlAccessor) =>
        {
            var rl = rlAccessor.Value;
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(RateLimitOptions.AuthPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rl.AuthPermitLimit, Window = TimeSpan.FromSeconds(rl.AuthWindowSeconds), QueueLimit = 0
                }));
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests",
                    Detail = "Too many attempts. Please wait a moment and try again."
                }, ct);
            };
        });

        // Same-origin frontend by default: with no origins configured the policy allows no cross-origin callers.
        services.AddOptions<CorsSettings>().BindConfiguration(CorsSettings.SectionName);
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((o, cs) =>
            o.AddPolicy(CorsSettings.PolicyName, p => p.WithOrigins(cs.Value.AllowedOrigins).AllowAnyHeader().AllowAnyMethod()));
        services.AddCors();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "BookIt API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
                Description = "Paste the token returned by /api/auth/login"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
            });
        });
        return services;
    }
}
