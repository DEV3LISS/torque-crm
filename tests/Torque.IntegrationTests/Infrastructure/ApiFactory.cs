using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Torque.Application.Abstractions;
using Torque.Application.Auth;
using Torque.Application.Catalog;
using Torque.Application.Users;

namespace Torque.IntegrationTests.Infrastructure;

/// <summary>
/// Starts a real PostgreSQL in Docker once for all tests, and the real API in memory on top of it.
/// Why not the EF InMemory provider: unique indexes, constraints and xmin concurrency only exist in
/// a real database — exactly the things we most need to test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin-Password-1";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Respawner _respawner = null!;

    /// <summary>Controllable "now" for expiry tests.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>Captures outgoing SMS/Telegram messages instead of sending them.</summary>
    public FakeNotificationSender Notifications { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "10000");
        builder.UseSetting("Outbox:Enabled", "false"); // tests drive the dispatcher explicitly

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<INotificationSender>();
            services.AddSingleton<INotificationSender>(Notifications);
        });
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _ = Services; // builds the host → runs migrations

        await using var conn = new NpgsqlConnection(_db.GetConnectionString());
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__ef_migrations_history"],
        });
    }

    /// <summary>Wipes all data and recreates the admin, so every test starts from the same state.</summary>
    public async Task ResetAsync()
    {
        await using (var conn = new NpgsqlConnection(_db.GetConnectionString()))
        {
            await conn.OpenAsync();
            await _respawner.ResetAsync(conn);
        }

        Clock.Reset();
        Notifications.Reset();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<UserService>()
            .CreateInitialAdminAsync(AdminEmail, "Test Admin", AdminPassword, CancellationToken.None);

        // Respawn also wiped the seeded opening hours: restore Mon–Sat 09:00–20:00, Sunday closed.
        await scope.ServiceProvider.GetRequiredService<CatalogService>().SetWorkingWeekAsync(
            Enum.GetValues<DayOfWeek>().Select(d => d == DayOfWeek.Sunday
                ? new WorkingDayDto(d, true, null, null)
                : new WorkingDayDto(d, false, new TimeOnly(9, 0), new TimeOnly(20, 0))).ToList(),
            CancellationToken.None);
    }

    public async Task<AuthResponse> LoginAsync(string email, string password)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password), Json.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(Json.Options))!;
    }

    public async Task<HttpClient> CreateClientAsAsync(string email, string password)
    {
        var auth = await LoginAsync(email, password);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    public Task<HttpClient> CreateAdminClientAsync() => CreateClientAsAsync(AdminEmail, AdminPassword);

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>Base class: shares one API + DB across tests in the collection and resets data before each test.</summary>
[Collection(ApiCollection.Name)]
public abstract class ApiTest(ApiFactory api) : IAsyncLifetime
{
    protected ApiFactory Api { get; } = api;

    public Task InitializeAsync() => Api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
