using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Torque.Domain.Users;
using Torque.Infrastructure.Auth;

namespace Torque.Api.Setup;

public static class Policies
{
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>Front desk work: customers, bookings, orders, payments.</summary>
    public const string FrontDesk = nameof(FrontDesk);

    public const string AuthRateLimit = "auth";
}

public static class AuthSetup
{
    public static IServiceCollection AddApiAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configure JwtBearer from the validated JwtOptions (same settings that issue the tokens).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false; // keep "sub" and "role" as they are in the token
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "name",
                    RoleClaimType = ClaimNames.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(Policies.FrontDesk, p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Manager)))
            // Secure by default: every endpoint requires a logged-in user unless it says [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var permitLimit = configuration.GetValue("RateLimiting:AuthPermitPerMinute", 5);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Brute-force protection for login/refresh: N attempts per minute per client IP.
            options.AddPolicy(Policies.AuthRateLimit, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
