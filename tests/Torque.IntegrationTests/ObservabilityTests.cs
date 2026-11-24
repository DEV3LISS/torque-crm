using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Torque.Domain.Messaging;
using Torque.Domain.Users;
using Torque.Infrastructure.Persistence;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests;

public class ObservabilityTests(ApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Metrics_endpoint_exposes_http_and_business_metrics()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var data = new TestData(Api, admin);
        var (customer, vehicle) = await data.CustomerWithVehicleAsync();
        await data.BookAsync(TestData.Booking(customer, vehicle, await data.BayAsync(), TestData.FutureWorkday(10), await data.ServiceAsync()));

        var metrics = await Api.CreateClient().GetStringAsync("/metrics");

        metrics.ShouldContain("http_server_request_duration_seconds");
        metrics.ShouldContain("torque_appointments_booked");
    }

    [Fact]
    public async Task Status_shows_degraded_outbox_and_is_admin_only()
    {
        using var admin = await Api.CreateAdminClientAsync();
        using var manager = await new TestData(Api, admin).StaffClientAsync(UserRole.Manager);
        (await manager.GetAsync("/health/status")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Simulate a reminder that failed for good.
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = OutboxMessage.Create("test", "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            for (var i = 0; i < OutboxMessage.MaxAttempts; i++)
            {
                message.MarkFailed("gateway down", DateTimeOffset.UtcNow);
            }

            db.Set<OutboxMessage>().Add(message);
            await db.SaveChangesAsync();
        }

        var response = await admin.GetAsync("/health/status");
        response.StatusCode.ShouldBe(HttpStatusCode.OK); // Degraded still answers 200 — the API works
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Degraded");
    }
}
