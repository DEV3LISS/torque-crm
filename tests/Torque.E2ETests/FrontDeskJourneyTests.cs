using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Torque.E2ETests;

/// <summary>Runs only when TORQUE_E2E_URL points at a running deployment (e.g. http://localhost:8080).</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TORQUE_E2E_URL")))
        {
            Skip = "Set TORQUE_E2E_URL to run end-to-end tests against a deployment.";
        }
    }
}

/// <summary>
/// A whole working day of the front desk, through the public API of the deployed container only:
/// new customer → free slot → booking → arrival → work order → mechanic → payment (+ retry) → report → audit.
/// Uses raw JSON on purpose: E2E tests must not share DTO classes with the server (that would hide contract breaks).
/// </summary>
public class FrontDeskJourneyTests
{
    private static readonly string BaseUrl = Environment.GetEnvironmentVariable("TORQUE_E2E_URL") ?? "";
    private static readonly string AdminEmail = Environment.GetEnvironmentVariable("TORQUE_E2E_ADMIN_EMAIL") ?? "admin@torque.local";
    private static readonly string AdminPassword = Environment.GetEnvironmentVariable("TORQUE_E2E_ADMIN_PASSWORD") ?? "ChangeMe-2026!";

    [E2EFact]
    public async Task Full_front_desk_journey()
    {
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var run = Random.Shared.Next(1_000_000, 9_999_999);

        // 1. Log in.
        var login = await Json(await api.PostAsJsonAsync("/api/v1/auth/login", new { email = AdminEmail, password = AdminPassword }));
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());

        // 2. New customer and car (phone typed with the domestic 8, plate in Cyrillic).
        var customer = await Json(await api.PostAsJsonAsync("/api/v1/customers",
            new { fullName = $"E2E Customer {run}", phone = $"8 913 {run}" }), HttpStatusCode.Created);
        var customerId = customer.GetProperty("id").GetString();
        customer.GetProperty("phone").GetString().ShouldBe($"+7913{run}");
        var vehicle = await Json(await api.PostAsJsonAsync($"/api/v1/customers/{customerId}/vehicles",
            new { make = "Kia", model = "Rio", year = 2020, plate = $"Е{run % 1000:D3}КХ{run % 100:D2}" }), HttpStatusCode.Created);
        var vehicleId = vehicle.GetProperty("id").GetString();

        // 3. Pick a service and a free slot ~5 days ahead.
        var service = (await Json(await api.GetAsync("/api/v1/services")))[0];
        var serviceId = service.GetProperty("id").GetString();
        var price = service.GetProperty("price").GetDecimal();
        var date = DateTime.UtcNow.Date.AddDays(5);
        if (date.DayOfWeek == DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        var availability = await Json(await api.GetAsync(
            $"/api/v1/availability?date={date:yyyy-MM-dd}&serviceIds={serviceId}"));
        var bay = availability.GetProperty("bays").EnumerateArray().First(b => b.GetProperty("slots").GetArrayLength() > 0);
        var slot = bay.GetProperty("slots")[Random.Shared.Next(bay.GetProperty("slots").GetArrayLength())].GetString();

        // 4. Book; booking the same slot again must be refused by the database.
        var booking = new { customerId, vehicleId, bayId = bay.GetProperty("bayId").GetString(), startsAt = slot, serviceIds = new[] { serviceId } };
        var appointment = await Json(await api.PostAsJsonAsync("/api/v1/appointments", booking), HttpStatusCode.Created);
        (await api.PostAsJsonAsync("/api/v1/appointments", booking)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var appointmentId = appointment.GetProperty("id").GetString();

        // 5. The car arrives → work order pre-filled with the booked service.
        await Json(await api.PostAsync($"/api/v1/appointments/{appointmentId}/check-in", null));
        var order = await Json(await api.PostAsJsonAsync("/api/v1/work-orders", new { appointmentId }), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetString();
        order.GetProperty("total").GetDecimal().ShouldBe(price);

        // 6. A mechanic does the work.
        var mechanic = await Json(await api.PostAsJsonAsync("/api/v1/users",
            new { email = $"mech{run}@e2e.local", fullName = $"Mechanic {run}", password = "E2E-mechanic-pass-1", role = "Mechanic" }), HttpStatusCode.Created);
        await Json(await api.PostAsJsonAsync($"/api/v1/work-orders/{orderId}/start", new { mechanicId = mechanic.GetProperty("id").GetString() }));
        await Json(await api.PostAsync($"/api/v1/work-orders/{orderId}/complete", null));

        // 7. Payment, then the "network glitch" retry with the same key.
        var key = Guid.NewGuid().ToString();
        var paid = await Json(await Pay(api, orderId!, price, key), HttpStatusCode.Created);
        paid.GetProperty("workOrder").GetProperty("status").GetString().ShouldBe("Closed");
        var retry = await Json(await Pay(api, orderId!, price, key));
        retry.GetProperty("replayed").GetBoolean().ShouldBeTrue();

        // 8. Owner sees the money today; the audit trail shows who did it.
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var revenue = await Json(await api.GetAsync($"/api/v1/reports/revenue?from={DateTime.UtcNow.AddDays(-1):yyyy-MM-dd}&to={today}"));
        revenue.GetProperty("total").GetDecimal().ShouldBeGreaterThanOrEqualTo(price);
        var audit = await Json(await api.GetAsync($"/api/v1/audit-logs?entityType=WorkOrder&entityId={orderId}"));
        audit.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(3);
    }

    [E2EFact]
    public async Task Deployment_is_healthy_and_observable()
    {
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        (await api.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.GetStringAsync("/metrics")).ShouldContain("http_server_request_duration_seconds");
        var docs = await api.GetAsync("/swagger/index.html");
        docs.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> Pay(HttpClient api, string orderId, decimal amount, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/work-orders/{orderId}/payments")
        {
            Content = JsonContent.Create(new { amount, method = "Card" }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return api.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonElement.Parse(body);
    }
}
