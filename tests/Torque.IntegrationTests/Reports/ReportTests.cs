using System.Net;
using System.Net.Http.Json;
using Torque.Application.Orders;
using Torque.Application.Reports;
using Torque.Domain.Orders;
using Torque.Domain.Users;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Reports;

public class ReportTests(ApiFactory api) : ApiTest(api)
{
    private static string Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TestData.BusinessOffset).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static HttpRequestMessage Money(Guid orderId, decimal amount, PaymentMethod method, string kind = "payments") =>
        new(HttpMethod.Post, $"/api/v1/work-orders/{orderId}/{kind}")
        {
            Content = JsonContent.Create(new PaymentRequest(amount, method), options: Json.Options),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };

    [Fact]
    public async Task Revenue_top_services_and_mechanics_reflect_real_money()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var data = new TestData(Api, admin);
        var (mechanic, _) = await data.StaffAsync(UserRole.Mechanic);
        var tyres = await data.ServiceAsync(30, 1000m);
        var balancing = await data.ServiceAsync(15, 500m);

        var first = await data.WorkOrderAsync(tyres, balancing); // 1500
        var second = await data.WorkOrderAsync(tyres);           // 1000
        foreach (var order in new[] { first, second })
        {
            (await admin.PostAsJsonAsync($"/api/v1/work-orders/{order.Id}/start", new StartWorkRequest(mechanic.Id), Json.Options)).EnsureSuccessStatusCode();
            (await admin.PostAsync($"/api/v1/work-orders/{order.Id}/complete", null)).EnsureSuccessStatusCode();
        }

        (await admin.SendAsync(Money(first.Id, 1500m, PaymentMethod.Card))).EnsureSuccessStatusCode();
        (await admin.SendAsync(Money(second.Id, 1000m, PaymentMethod.Cash))).EnsureSuccessStatusCode();
        (await admin.SendAsync(Money(second.Id, 200m, PaymentMethod.Cash, "refunds"))).EnsureSuccessStatusCode();

        var revenue = (await admin.GetFromJsonAsync<RevenueReport>($"/api/v1/reports/revenue?from={Today}&to={Today}", Json.Options))!;
        revenue.Total.ShouldBe(2300m);
        var day = revenue.Rows.Single();
        day.Payments.ShouldBe(2500m);
        day.Refunds.ShouldBe(200m);
        day.PaymentCount.ShouldBe(2);
        revenue.ByMethod.Single(m => m.Method == "Card").Net.ShouldBe(1500m);
        revenue.ByMethod.Single(m => m.Method == "Cash").Net.ShouldBe(800m);

        var top = (await admin.GetFromJsonAsync<List<TopServiceRow>>($"/api/v1/reports/top-services?from={Today}&to={Today}", Json.Options))!;
        top[0].Name.ShouldBe(tyres.Name);
        top[0].TimesSold.ShouldBe(2);
        top[0].Revenue.ShouldBe(2000m);

        var mechanics = (await admin.GetFromJsonAsync<List<MechanicWorkloadRow>>($"/api/v1/reports/mechanics?from={Today}&to={Today}", Json.Options))!;
        mechanics.Single().CompletedOrders.ShouldBe(2);
        mechanics.Single().Revenue.ShouldBe(2500m);
    }

    [Fact]
    public async Task Bay_utilization_is_booked_time_over_opening_time()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var data = new TestData(Api, admin);
        var bay = await data.BayAsync();
        var emptyBay = await data.BayAsync();
        var (customer, vehicle) = await data.CustomerWithVehicleAsync();
        var start = TestData.FutureWorkday(10);
        await data.BookAsync(TestData.Booking(customer, vehicle, bay, start, await data.ServiceAsync(60)));
        var date = TestData.LocalDate(start).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var rows = (await admin.GetFromJsonAsync<List<BayUtilizationRow>>($"/api/v1/reports/bay-utilization?from={date}&to={date}", Json.Options))!;

        var row = rows.Single(r => r.BayId == bay.Id);
        row.BookedMinutes.ShouldBe(60);
        row.OpenMinutes.ShouldBe(660); // 09:00–20:00
        row.UtilizationPercent.ShouldBe(9.1m);

        // Regression: a bay without bookings used to show 100%+ (LEAST/GREATEST ignore NULLs in PostgreSQL).
        var empty = rows.Single(r => r.BayId == emptyBay.Id);
        empty.BookedMinutes.ShouldBe(0);
        empty.UtilizationPercent.ShouldBe(0m);
    }

    [Fact]
    public async Task Reports_are_for_the_owner_only_and_validate_the_period()
    {
        using var admin = await Api.CreateAdminClientAsync();
        using var manager = await new TestData(Api, admin).StaffClientAsync(UserRole.Manager);

        (await manager.GetAsync($"/api/v1/reports/revenue?from={Today}&to={Today}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var backwards = await admin.GetAsync("/api/v1/reports/revenue?from=2026-12-31&to=2026-01-01");
        await backwards.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }
}
