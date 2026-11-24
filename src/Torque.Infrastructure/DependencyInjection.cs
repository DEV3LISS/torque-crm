using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Torque.Application.Abstractions;
using Torque.Application.Reports;
using Torque.Infrastructure.Auth;
using Torque.Infrastructure.Messaging;
using Torque.Infrastructure.Persistence;
using Torque.Infrastructure.Persistence.Interceptors;
using Torque.Infrastructure.Reports;

namespace Torque.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TimestampsInterceptor>();
        services.AddScoped<AuditInterceptor>(); // scoped: needs the current user of the request

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            ConfigureDbContext(options, connectionString);
            // Order matters: timestamps are set first, then the audit captures the final state.
            options.AddInterceptors(sp.GetRequiredService<TimestampsInterceptor>(), sp.GetRequiredService<AuditInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);
        services.AddOptions<BusinessOptions>()
            .BindConfiguration(BusinessOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Unknown Business:TimeZone.")
            .ValidateOnStart();
        services.AddSingleton<IBusinessTimeZone, BusinessTimeZone>();

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddScoped<IReportQueries, ReportQueries>();

        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName);
        services.AddScoped<OutboxDispatcher>();
        services.AddHostedService<OutboxWorker>();
        services.AddSingleton<INotificationSender, LoggingNotificationSender>();

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
