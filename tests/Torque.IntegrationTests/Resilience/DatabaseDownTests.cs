using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Torque.Application.Auth;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Resilience;

/// <summary>The API started, but PostgreSQL is unreachable (port with nothing listening).</summary>
public sealed class DatabaseDownFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Outbox:Enabled", "false");
    }
}

public class DatabaseDownTests(DatabaseDownFactory api) : IClassFixture<DatabaseDownFactory>
{
    [Fact]
    public async Task Process_is_alive_but_not_ready()
    {
        using var client = api.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // A load balancer stops sending traffic here; an orchestrator does NOT restart the container
        // (restarting would not fix a database outage).
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Requests_get_503_with_retry_after_not_500()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@test.local", "whatever-password"));

        await response.ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull();
    }
}
