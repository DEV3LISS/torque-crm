using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Torque.Application.Catalog;
using Torque.Application.Customers;
using Torque.Application.Scheduling;
using Torque.Domain.Messaging;
using Torque.Infrastructure.Messaging;
using Torque.Infrastructure.Persistence;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Messaging;

public class ReminderTests(ApiFactory api) : ApiTest(api), IAsyncLifetime
{
    private HttpClient _admin = null!;
    private TestData _data = null!;
    private BayDto _bay = null!;
    private ServiceDto _service = null!;
    private CustomerDto _customer = null!;
    private VehicleDto _vehicle = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        _admin = await Api.CreateAdminClientAsync();
        _data = new TestData(Api, _admin);
        _bay = await _data.BayAsync();
        _service = await _data.ServiceAsync(30);
        (_customer, _vehicle) = await _data.CustomerWithVehicleAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        _admin.Dispose();
        return Task.CompletedTask;
    }

    private Task<AppointmentDto> Book(DateTimeOffset start) =>
        _data.BookAsync(TestData.Booking(_customer, _vehicle, _bay, start, _service));

    private async Task<int> DispatchAsync()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().ProcessBatchAsync(CancellationToken.None);
    }

    private async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OutboxMessage>().AsNoTracking()
            .OrderBy(m => m.CreatedAt).ToListAsync();
    }

    /// <summary>Move the test clock to 23 hours before the appointment — the reminder is due.</summary>
    private void TravelToDayBefore(DateTimeOffset start) => Api.Clock.Advance(start - Api.Clock.GetUtcNow() - TimeSpan.FromHours(23));

    [Fact]
    public async Task Reminder_is_sent_once_the_day_before()
    {
        var start = TestData.FutureWorkday(10);
        await Book(start);

        (await DispatchAsync()).ShouldBe(0); // not due yet
        Api.Notifications.Sent.ShouldBeEmpty();

        TravelToDayBefore(start);
        (await DispatchAsync()).ShouldBe(1);
        (await DispatchAsync()).ShouldBe(0); // never twice

        var sms = Api.Notifications.Sent.Single();
        sms.Phone.ShouldBe(_customer.Phone);
        sms.Text.ShouldContain("10:00"); // local business time, not UTC
        (await OutboxAsync()).Single().Status.ShouldBe(OutboxStatus.Sent);
    }

    [Fact]
    public async Task Cancelled_appointment_gets_no_reminder()
    {
        var start = TestData.FutureWorkday(11);
        var appointment = await Book(start);
        (await _admin.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/cancel", new CancelRequest(null), Json.Options)).EnsureSuccessStatusCode();

        TravelToDayBefore(start);
        await DispatchAsync();

        Api.Notifications.Sent.ShouldBeEmpty();
        (await OutboxAsync()).Single().Status.ShouldBe(OutboxStatus.Skipped);
    }

    [Fact]
    public async Task Rescheduled_appointment_is_reminded_once_with_the_new_time()
    {
        var start = TestData.FutureWorkday(10);
        var appointment = await Book(start);
        (await _admin.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/reschedule",
            new RescheduleRequest(_bay.Id, start.AddHours(5)), Json.Options)).EnsureSuccessStatusCode();

        TravelToDayBefore(start); // both reminders are due by now
        Api.Clock.Advance(TimeSpan.FromHours(6));
        await DispatchAsync();

        Api.Notifications.Sent.Single().Text.ShouldContain("15:00");
        (await OutboxAsync()).Select(m => m.Status).ShouldBe([OutboxStatus.Skipped, OutboxStatus.Sent]);
    }

    [Fact]
    public async Task Gateway_outage_is_retried_later_and_nothing_is_lost()
    {
        var start = TestData.FutureWorkday(12);
        await Book(start);
        TravelToDayBefore(start);

        Api.Notifications.FailAll = true;
        await DispatchAsync();
        var failed = (await OutboxAsync()).Single();
        failed.Status.ShouldBe(OutboxStatus.Pending);
        failed.Attempts.ShouldBe(1);
        failed.LastError.ShouldNotBeNull().ShouldContain("unavailable");

        Api.Notifications.FailAll = false;
        (await DispatchAsync()).ShouldBe(0); // backing off — not due yet
        Api.Clock.Advance(TimeSpan.FromMinutes(2));
        await DispatchAsync();

        Api.Notifications.Sent.Count.ShouldBe(1);
        (await OutboxAsync()).Single().Status.ShouldBe(OutboxStatus.Sent);
    }

    [Fact]
    public async Task Parallel_workers_deliver_every_message_exactly_once()
    {
        // Like 3 API instances running the worker at the same time.
        for (var hour = 9; hour < 15; hour++)
        {
            await Book(TestData.FutureWorkday(hour));
        }

        Api.Clock.Advance(TimeSpan.FromDays(30));
        await Task.WhenAll(DispatchAsync(), DispatchAsync(), DispatchAsync());
        await DispatchAsync();

        Api.Notifications.Sent.Count.ShouldBe(6);
        Api.Notifications.Sent.Select(n => n.MessageId).Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task Last_minute_booking_gets_no_reminder()
    {
        // Pretend it is 1 hour before a slot.
        var start = TestData.FutureWorkday(10);
        Api.Clock.Advance(start - Api.Clock.GetUtcNow() - TimeSpan.FromHours(1));

        await Book(start);

        (await OutboxAsync()).ShouldBeEmpty();
    }
}
