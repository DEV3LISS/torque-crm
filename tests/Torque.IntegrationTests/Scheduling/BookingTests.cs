using System.Net;
using System.Net.Http.Json;
using Torque.Application.Catalog;
using Torque.Application.Customers;
using Torque.Application.Scheduling;
using Torque.Domain.Scheduling;
using Torque.Domain.Users;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Scheduling;

public class BookingTests(ApiFactory api) : ApiTest(api), IAsyncLifetime
{
    private HttpClient _admin = null!;
    private TestData _data = null!;
    private BayDto _bay = null!;
    private ServiceDto _tyres = null!; // 30 min
    private CustomerDto _customer = null!;
    private VehicleDto _vehicle = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        _admin = await Api.CreateAdminClientAsync();
        _data = new TestData(Api, _admin);
        _bay = await _data.BayAsync();
        _tyres = await _data.ServiceAsync(30);
        (_customer, _vehicle) = await _data.CustomerWithVehicleAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        _admin.Dispose();
        return Task.CompletedTask;
    }

    private BookAppointmentRequest At(DateTimeOffset start, BayDto? bay = null) =>
        TestData.Booking(_customer, _vehicle, bay ?? _bay, start, _tyres);

    [Fact]
    public async Task Valid_booking_returns_201_and_shows_on_the_board()
    {
        var start = TestData.FutureWorkday(10);

        var response = await _admin.PostAsJsonAsync("/api/v1/appointments", At(start), Json.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<AppointmentDto>(Json.Options))!;
        created.EndsAt.ShouldBe(start.AddMinutes(30));
        created.CustomerName.ShouldBe(_customer.FullName);
        created.VehiclePlate.ShouldBe(_vehicle.Plate);

        var board = await _admin.GetFromJsonAsync<List<AppointmentDto>>(
            $"/api/v1/appointments?from={Uri.EscapeDataString(start.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(start.AddHours(2).ToString("O"))}",
            Json.Options);
        board!.ShouldContain(a => a.Id == created.Id);
    }

    [Fact]
    public async Task Overlapping_booking_in_the_same_bay_is_rejected_by_the_database()
    {
        var start = TestData.FutureWorkday(10);
        await _data.BookAsync(At(start));

        // 10:15–10:45 overlaps 10:00–10:30
        var response = await _admin.PostAsJsonAsync("/api/v1/appointments", At(start.AddMinutes(15)), Json.Options);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
        problem.GetProperty("title").GetString().ShouldBe("Time slot taken");
    }

    [Fact]
    public async Task Back_to_back_bookings_and_other_bays_are_allowed()
    {
        var start = TestData.FutureWorkday(10);
        var otherBay = await _data.BayAsync();
        await _data.BookAsync(At(start));

        (await _admin.PostAsJsonAsync("/api/v1/appointments", At(start.AddMinutes(30)), Json.Options))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await _admin.PostAsJsonAsync("/api/v1/appointments", At(start, otherBay), Json.Options))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cancelling_frees_the_slot()
    {
        var start = TestData.FutureWorkday(11);
        var first = await _data.BookAsync(At(start));

        (await _admin.PostAsJsonAsync($"/api/v1/appointments/{first.Id}/cancel", new CancelRequest("Customer called"), Json.Options))
            .EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync("/api/v1/appointments", At(start), Json.Options))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Ten_simultaneous_bookings_of_one_slot_produce_exactly_one_appointment()
    {
        // The race a C# "is it free? then insert" check cannot win: all 10 requests would see "free".
        var start = TestData.FutureWorkday(12);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var client = await Api.CreateAdminClientAsync();
            return await client.PostAsJsonAsync("/api/v1/appointments", At(start), Json.Options);
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        // Losers get 409 — either "slot taken" (exclusion violation) or, occasionally, a deadlock victim
        // (concurrent inserts into an exclusion constraint can deadlock); never a 500.
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);
    }

    [Fact]
    public async Task Business_rules_return_422()
    {
        var sunday = TestData.FutureWorkday(10);
        while (sunday.DayOfWeek != DayOfWeek.Sunday)
        {
            sunday = sunday.AddDays(1);
        }

        foreach (var start in new[]
        {
            TestData.FutureWorkday(19, 45),     // ends 20:15, after closing
            TestData.FutureWorkday(10, 10),     // off the 15-minute grid
            sunday,                             // closed
            DateTimeOffset.UtcNow.AddDays(-1),  // past
        })
        {
            var response = await _admin.PostAsJsonAsync("/api/v1/appointments", At(start), Json.Options);
            await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
        }
    }

    [Fact]
    public async Task Vehicle_must_belong_to_the_customer()
    {
        var (_, someoneElsesCar) = await _data.CustomerWithVehicleAsync();
        var request = TestData.Booking(_customer, someoneElsesCar, _bay, TestData.FutureWorkday(10), _tyres);

        var response = await _admin.PostAsJsonAsync("/api/v1/appointments", request, Json.Options);

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Deactivated_service_cannot_be_booked()
    {
        (await _admin.PostAsync($"/api/v1/services/{_tyres.Id}/deactivate", null)).EnsureSuccessStatusCode();

        var response = await _admin.PostAsJsonAsync("/api/v1/appointments", At(TestData.FutureWorkday(10)), Json.Options);

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Mechanic_moves_the_car_through_the_bay_but_cannot_book()
    {
        using var mechanic = await _data.StaffClientAsync(UserRole.Mechanic);
        var appointment = await _data.BookAsync(At(TestData.FutureWorkday(14)));

        (await mechanic.PostAsJsonAsync("/api/v1/appointments", At(TestData.FutureWorkday(15)), Json.Options))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await mechanic.PostAsync($"/api/v1/appointments/{appointment.Id}/check-in", null)).EnsureSuccessStatusCode();
        var done = (await (await mechanic.PostAsync($"/api/v1/appointments/{appointment.Id}/complete", null))
            .Content.ReadFromJsonAsync<AppointmentDto>(Json.Options))!;
        done.Status.ShouldBe(AppointmentStatus.Completed);
    }

    [Fact]
    public async Task Invalid_status_transitions_return_409()
    {
        var appointment = await _data.BookAsync(At(TestData.FutureWorkday(16)));

        var completeTooEarly = await _admin.PostAsync($"/api/v1/appointments/{appointment.Id}/complete", null);
        await completeTooEarly.ShouldBeProblemAsync(HttpStatusCode.Conflict);

        var noShowBeforeStart = await _admin.PostAsync($"/api/v1/appointments/{appointment.Id}/no-show", null);
        await noShowBeforeStart.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reschedule_into_a_taken_slot_is_rejected()
    {
        var a = await _data.BookAsync(At(TestData.FutureWorkday(10)));
        await _data.BookAsync(At(TestData.FutureWorkday(11)));

        var response = await _admin.PostAsJsonAsync($"/api/v1/appointments/{a.Id}/reschedule",
            new RescheduleRequest(_bay.Id, TestData.FutureWorkday(11, 15)), Json.Options);

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Availability_hides_booked_time_and_sums_durations()
    {
        var balancing = await _data.ServiceAsync(15);
        var start = TestData.FutureWorkday(10);
        await _data.BookAsync(At(start)); // 10:00–10:30 taken

        var availability = (await _admin.GetFromJsonAsync<AvailabilityDto>(
            $"/api/v1/availability?date={TestData.LocalDate(start):yyyy-MM-dd}&serviceIds={_tyres.Id}&serviceIds={balancing.Id}&bayId={_bay.Id}",
            Json.Options))!;

        availability.DurationMinutes.ShouldBe(45);
        var slots = availability.Bays.Single().Slots.Select(s => s.ToOffset(TestData.BusinessOffset).TimeOfDay).ToList();
        slots.ShouldContain(TimeSpan.FromHours(9));                 // 09:00–09:45
        slots.ShouldNotContain(new TimeSpan(9, 30, 0));             // 09:30–10:15 overlaps
        slots.ShouldContain(new TimeSpan(10, 30, 0));               // right after the booking
        slots.ShouldNotContain(new TimeSpan(19, 30, 0));            // would end 20:15
        slots[^1].ShouldBe(new TimeSpan(19, 15, 0));
    }

    [Fact]
    public async Task Bay_with_upcoming_appointments_cannot_be_deactivated()
    {
        await _data.BookAsync(At(TestData.FutureWorkday(10)));

        var response = await _admin.PostAsync($"/api/v1/bays/{_bay.Id}/deactivate", null);

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }
}
