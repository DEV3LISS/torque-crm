using System.Net.Http.Json;
using Torque.Application.Catalog;
using Torque.Application.Customers;
using Torque.Application.Orders;
using Torque.Application.Scheduling;
using Torque.Domain.Users;
using Torque.Application.Users;

namespace Torque.IntegrationTests.Infrastructure;

/// <summary>Creates test data through the public API (as a real client would), so tests exercise the full stack.</summary>
public sealed class TestData(ApiFactory api, HttpClient admin)
{
    public static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(5); // Asia/Yekaterinburg, no DST

    private int _counter;

    /// <summary>A working day (Mon–Sat) at least 3 days ahead, at the given local time.</summary>
    public static DateTimeOffset FutureWorkday(int hour, int minute = 0)
    {
        var date = DateTimeOffset.UtcNow.ToOffset(BusinessOffset).Date.AddDays(3);
        while (date.DayOfWeek == DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, BusinessOffset);
    }

    public static DateOnly LocalDate(DateTimeOffset t) => DateOnly.FromDateTime(t.ToOffset(BusinessOffset).DateTime);

    public async Task<BayDto> BayAsync(string? name = null) =>
        await PostAsync<BayDto>("/api/v1/bays", new SaveBayRequest(name ?? $"Bay {Next()}"));

    public async Task<ServiceDto> ServiceAsync(int minutes = 30, decimal price = 1500m) =>
        await PostAsync<ServiceDto>("/api/v1/services", new SaveServiceRequest($"Service {Next()}", "Tyres", minutes, price));

    public async Task<(CustomerDto Customer, VehicleDto Vehicle)> CustomerWithVehicleAsync()
    {
        var n = Next();
        var customer = await PostAsync<CustomerDto>("/api/v1/customers",
            new SaveCustomerRequest($"Customer {n}", $"+7912{n:D7}", null, null));
        var vehicle = await PostAsync<VehicleDto>($"/api/v1/customers/{customer.Id}/vehicles",
            new SaveVehicleRequest("Lada", "Vesta", 2021, $"A{n:D3}BC96", null));
        return (customer, vehicle);
    }

    public static BookAppointmentRequest Booking(CustomerDto c, VehicleDto v, BayDto bay, DateTimeOffset start, params ServiceDto[] services) =>
        new(c.Id, v.Id, bay.Id, start, services.Select(s => s.Id).ToList(), null);

    public async Task<AppointmentDto> BookAsync(BookAppointmentRequest request) =>
        await PostAsync<AppointmentDto>("/api/v1/appointments", request);

    public async Task<HttpClient> StaffClientAsync(UserRole role) => (await StaffAsync(role)).Client;

    public async Task<(UserDto User, HttpClient Client)> StaffAsync(UserRole role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}{Next()}@test.local";
        const string password = "Staff-Password-1";
        var user = await PostAsync<UserDto>("/api/v1/users", new CreateUserRequest(email, email, password, role));
        return (user, await api.CreateClientAsAsync(email, password));
    }

    /// <summary>Walk-in work order with the given service lines.</summary>
    public async Task<WorkOrderDto> WorkOrderAsync(params ServiceDto[] services)
    {
        var (customer, vehicle) = await CustomerWithVehicleAsync();
        var order = await PostAsync<WorkOrderDto>("/api/v1/work-orders", new OpenWorkOrderRequest(null, customer.Id, vehicle.Id));
        foreach (var service in services)
        {
            order = await PostAsync<WorkOrderDto>($"/api/v1/work-orders/{order.Id}/lines/services", new AddServiceLineRequest(service.Id));
        }

        return order;
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await admin.PostAsJsonAsync(url, body, Json.Options);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private int Next() => Interlocked.Increment(ref _counter);
}
