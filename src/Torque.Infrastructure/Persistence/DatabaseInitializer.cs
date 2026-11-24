using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Torque.Application.Users;

namespace Torque.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations at startup. Handy for local/demo runs; in real production
    /// migrations run as a separate deployment step.</summary>
    public bool MigrateOnStartup { get; init; }
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? AdminEmail { get; init; }
    public string? AdminPassword { get; init; }
    public string AdminFullName { get; init; } = "Administrator";

    /// <summary>Fill bays, services and sample customers for demos.</summary>
    public bool DemoData { get; init; }
}

public static partial class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
        {
            await sp.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);
            LogMigrated(logger);
        }

        var seed = sp.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(seed.AdminEmail) && !string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            var created = await sp.GetRequiredService<UserService>()
                .CreateInitialAdminAsync(seed.AdminEmail, seed.AdminFullName, seed.AdminPassword, ct);
            if (created)
            {
                LogAdminSeeded(logger);
            }
        }

        if (seed.DemoData)
        {
            var year = sp.GetRequiredService<TimeProvider>().GetUtcNow().Year;
            if (await DemoDataSeeder.SeedAsync(sp.GetRequiredService<AppDbContext>(), year, ct))
            {
                LogDemoSeeded(logger);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations applied")]
    private static partial void LogMigrated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initial admin user created from Seed settings")]
    private static partial void LogAdminSeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data created")]
    private static partial void LogDemoSeeded(ILogger logger);
}
