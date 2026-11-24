using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Torque.Application.Auditing;
using Torque.Application.Catalog;
using Torque.Application.Common;
using Torque.Application.Customers;
using Torque.Application.Orders;
using Torque.Application.Scheduling;
using Torque.Domain.Orders;
using Torque.Domain.Users;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Orders;

public class WorkOrderTests(ApiFactory api) : ApiTest(api), IAsyncLifetime
{
    private HttpClient _admin = null!;
    private TestData _data = null!;
    private ServiceDto _service = null!; // 1000.00

    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        _admin = await Api.CreateAdminClientAsync();
        _data = new TestData(Api, _admin);
        _service = await _data.ServiceAsync(30, 1000m);
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        _admin.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;

    private static HttpRequestMessage Payment(Guid orderId, decimal amount, string key, string kind = "payments") =>
        new(HttpMethod.Post, $"/api/v1/work-orders/{orderId}/{kind}")
        {
            Content = JsonContent.Create(new PaymentRequest(amount, PaymentMethod.Card), options: Json.Options),
            Headers = { { "Idempotency-Key", key } },
        };

    private async Task<WorkOrderDto> CompletedOrderAsync()
    {
        var (mechanic, _) = await _data.StaffAsync(UserRole.Mechanic);
        var order = await _data.WorkOrderAsync(_service);
        (await _admin.PostAsJsonAsync($"/api/v1/work-orders/{order.Id}/start", new StartWorkRequest(mechanic.Id), Json.Options)).EnsureSuccessStatusCode();
        return await Read<WorkOrderDto>(await _admin.PostAsync($"/api/v1/work-orders/{order.Id}/complete", null));
    }

    [Fact]
    public async Task Order_from_appointment_is_prefilled_and_keeps_the_price_snapshot()
    {
        var (customer, vehicle) = await _data.CustomerWithVehicleAsync();
        var appointment = await _data.BookAsync(TestData.Booking(customer, vehicle, await _data.BayAsync(), TestData.FutureWorkday(10), _service));
        (await _admin.PostAsync($"/api/v1/appointments/{appointment.Id}/check-in", null)).EnsureSuccessStatusCode();

        var created = await _admin.PostAsJsonAsync("/api/v1/work-orders", new OpenWorkOrderRequest(appointment.Id, null, null), Json.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = await Read<WorkOrderDto>(created);
        order.Number.ShouldStartWith("WO-");
        order.Lines.Single().UnitPrice.ShouldBe(1000m);

        // The owner raises the price list — the existing order must not change.
        (await _admin.PutAsJsonAsync($"/api/v1/services/{_service.Id}",
            new SaveServiceRequest(_service.Name, _service.Category, 30, 1500m), Json.Options)).EnsureSuccessStatusCode();
        var reloaded = await _admin.GetFromJsonAsync<WorkOrderDto>($"/api/v1/work-orders/{order.Id}", Json.Options);
        reloaded!.Total.ShouldBe(1000m);

        // Second order for the same appointment is refused.
        var duplicate = await _admin.PostAsJsonAsync("/api/v1/work-orders", new OpenWorkOrderRequest(appointment.Id, null, null), Json.Options);
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Retried_payment_with_the_same_key_is_not_charged_twice()
    {
        var order = await CompletedOrderAsync();
        var key = Guid.NewGuid().ToString();

        var first = await _admin.SendAsync(Payment(order.Id, 400m, key));
        var retry = await _admin.SendAsync(Payment(order.Id, 400m, key));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read<PaymentResult>(retry)).Payment.Id.ShouldBe((await Read<PaymentResult>(first)).Payment.Id);

        var reloaded = await _admin.GetFromJsonAsync<WorkOrderDto>($"/api/v1/work-orders/{order.Id}", Json.Options);
        reloaded!.PaidAmount.ShouldBe(400m);
        reloaded.Payments.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_with_a_different_amount_is_rejected()
    {
        var order = await CompletedOrderAsync();
        var key = Guid.NewGuid().ToString();
        (await _admin.SendAsync(Payment(order.Id, 400m, key))).EnsureSuccessStatusCode();

        var response = await _admin.SendAsync(Payment(order.Id, 500m, key));

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Payment_without_idempotency_key_is_rejected()
    {
        var order = await CompletedOrderAsync();

        var response = await _admin.PostAsJsonAsync($"/api/v1/work-orders/{order.Id}/payments",
            new PaymentRequest(100m, PaymentMethod.Cash), Json.Options);

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Two_cashiers_paying_the_full_amount_at_once_cannot_overpay()
    {
        // Without the row lock both would read "due 1000", both pass the check, and the customer pays 2000.
        var order = await CompletedOrderAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var client = await Api.CreateAdminClientAsync();
            return await client.SendAsync(Payment(order.Id, 1000m, Guid.NewGuid().ToString()));
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created
            || r.StatusCode == HttpStatusCode.UnprocessableEntity
            || r.StatusCode == HttpStatusCode.Conflict);

        var reloaded = await _admin.GetFromJsonAsync<WorkOrderDto>($"/api/v1/work-orders/{order.Id}", Json.Options);
        reloaded!.PaidAmount.ShouldBe(1000m);
        reloaded.Status.ShouldBe(WorkOrderStatus.Closed);
    }

    [Fact]
    public async Task Parallel_retries_with_one_key_create_one_payment()
    {
        var order = await CompletedOrderAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var client = await Api.CreateAdminClientAsync();
            return await client.SendAsync(Payment(order.Id, 300m, key));
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.ShouldAllBe(r => r.IsSuccessStatusCode);
        var reloaded = await _admin.GetFromJsonAsync<WorkOrderDto>($"/api/v1/work-orders/{order.Id}", Json.Options);
        reloaded!.PaidAmount.ShouldBe(300m);
    }

    [Fact]
    public async Task Concurrent_line_edits_never_leave_wrong_totals()
    {
        var order = await _data.WorkOrderAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            using var client = await Api.CreateAdminClientAsync();
            return await client.PostAsJsonAsync($"/api/v1/work-orders/{order.Id}/lines/parts",
                new AddPartLineRequest($"Part {i}", 1, 100m), Json.Options);
        }));

        // Some may lose the optimistic-concurrency race (409) — but never silently.
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        var reloaded = await _admin.GetFromJsonAsync<WorkOrderDto>($"/api/v1/work-orders/{order.Id}", Json.Options);
        reloaded!.Subtotal.ShouldBe(reloaded.Lines.Sum(l => l.LineTotal));
        reloaded.Lines.Count.ShouldBe(responses.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact]
    public async Task Manager_needs_admin_for_a_big_discount()
    {
        var order = await _data.WorkOrderAsync(_service);
        using var manager = await _data.StaffClientAsync(UserRole.Manager);

        (await manager.PutAsJsonAsync($"/api/v1/work-orders/{order.Id}/discount", new DiscountRequest(DiscountType.Percent, 10), Json.Options))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var tooBig = await manager.PutAsJsonAsync($"/api/v1/work-orders/{order.Id}/discount", new DiscountRequest(DiscountType.Percent, 30), Json.Options);
        await tooBig.ShouldBeProblemAsync(HttpStatusCode.Forbidden);

        var byAdmin = await _admin.PutAsJsonAsync($"/api/v1/work-orders/{order.Id}/discount", new DiscountRequest(DiscountType.Percent, 30), Json.Options);
        (await Read<WorkOrderDto>(byAdmin)).Total.ShouldBe(700m);
    }

    [Fact]
    public async Task Mechanic_cannot_touch_another_mechanics_order_even_knowing_its_id()
    {
        // IDOR: the id is visible in URLs and logs; authorization must not rely on it being secret.
        var (_, mechanicA) = await _data.StaffAsync(UserRole.Mechanic);
        using var mechanicB = await _data.StaffClientAsync(UserRole.Mechanic);
        var order = await _data.WorkOrderAsync(_service);
        (await mechanicA.PostAsJsonAsync($"/api/v1/work-orders/{order.Id}/start", new StartWorkRequest(null), Json.Options)).EnsureSuccessStatusCode();

        var response = await mechanicB.PostAsJsonAsync($"/api/v1/work-orders/{order.Id}/lines/parts",
            new AddPartLineRequest("Something", 1, 1m), Json.Options);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden);

        var listB = await mechanicB.GetFromJsonAsync<PagedResult<WorkOrderSummaryDto>>("/api/v1/work-orders", Json.Options);
        listB!.Items.ShouldNotContain(o => o.Id == order.Id);
        mechanicA.Dispose();
    }

    [Fact]
    public async Task Mechanic_cannot_take_payments()
    {
        var order = await CompletedOrderAsync();
        using var mechanic = await _data.StaffClientAsync(UserRole.Mechanic);

        (await mechanic.SendAsync(Payment(order.Id, 100m, Guid.NewGuid().ToString()))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Refund_is_admin_only_and_reopens_the_order()
    {
        var order = await CompletedOrderAsync();
        (await _admin.SendAsync(Payment(order.Id, 1000m, Guid.NewGuid().ToString()))).EnsureSuccessStatusCode();
        using var manager = await _data.StaffClientAsync(UserRole.Manager);

        (await manager.SendAsync(Payment(order.Id, 200m, Guid.NewGuid().ToString(), "refunds"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var refund = await _admin.SendAsync(Payment(order.Id, 200m, Guid.NewGuid().ToString(), "refunds"));
        refund.StatusCode.ShouldBe(HttpStatusCode.Created);
        var result = await Read<PaymentResult>(refund);
        result.WorkOrder.Status.ShouldBe(WorkOrderStatus.Completed);
        result.WorkOrder.AmountDue.ShouldBe(200m);
    }

    [Fact]
    public async Task Changes_are_audited_with_user_and_masked_personal_data()
    {
        var (customer, _) = await _data.CustomerWithVehicleAsync();
        var me = await _admin.GetFromJsonAsync<Torque.Application.Users.UserDto>("/api/v1/users/me", Json.Options);

        (await _admin.PutAsJsonAsync($"/api/v1/customers/{customer.Id}",
            new SaveCustomerRequest(customer.FullName, "+79990001122", null, null), Json.Options)).EnsureSuccessStatusCode();

        var logs = await _admin.GetFromJsonAsync<PagedResult<AuditLogDto>>(
            $"/api/v1/audit-logs?entityType=Customer&entityId={customer.Id}", Json.Options);
        var update = logs!.Items.First(l => l.Action == Torque.Domain.Auditing.AuditAction.Updated);
        update.UserId.ShouldBe(me!.Id);
        update.CorrelationId.ShouldNotBeNullOrEmpty();

        var phone = JsonDocument.Parse(update.Changes).RootElement.GetProperty("Phone");
        phone.GetProperty("new").GetString().ShouldBe("**********22");
        update.Changes.ShouldNotContain("79990001122");
    }

    [Fact]
    public async Task Password_hashes_never_reach_the_audit_log()
    {
        var logs = await _admin.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/v1/audit-logs?entityType=User", Json.Options);

        logs!.Items.ShouldNotBeEmpty();
        logs.Items.ShouldAllBe(l => !l.Changes.Contains("AQAAAA")); // PBKDF2 hashes start with this prefix
    }
}
