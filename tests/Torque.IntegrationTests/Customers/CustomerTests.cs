using System.Net;
using System.Net.Http.Json;
using Torque.Application.Common;
using Torque.Application.Customers;
using Torque.Domain.Users;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Customers;

public class CustomerTests(ApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Phone_is_stored_once_and_found_in_any_format()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var created = await Create(admin, "Ivan Petrov", "8 (912) 345-67-89");
        created.Phone.ShouldBe("+79123456789");

        foreach (var search in new[] { "+7 912 345 67 89", "89123456789", "3456789", "8912", "8 (912) 34" })
        {
            var found = await SearchAsync(admin, search);
            found.Items.ShouldContain(c => c.Id == created.Id, $"search '{search}'");
        }
    }

    [Fact]
    public async Task Same_phone_in_another_format_is_a_duplicate()
    {
        using var admin = await Api.CreateAdminClientAsync();
        await Create(admin, "Ivan", "+79123456789");

        var response = await admin.PostAsJsonAsync("/api/v1/customers",
            new SaveCustomerRequest("Other Ivan", "8 912 345-67-89", null, null), Json.Options);

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Name_search_is_case_insensitive_and_wildcards_are_literal()
    {
        using var admin = await Api.CreateAdminClientAsync();
        await Create(admin, "Maria Sokolova", "+79120000001");
        await Create(admin, "Oleg Ivanov", "+79120000002");

        (await SearchAsync(admin, "sokol")).Items.Single().FullName.ShouldBe("Maria Sokolova");
        (await SearchAsync(admin, "%")).TotalCount.ShouldBe(0); // '%' must not act as "match everything"
    }

    [Fact]
    public async Task Plate_typed_in_cyrillic_is_found_by_latin_and_must_be_unique()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var customer = await Create(admin, "Owner", "+79120000003");
        var add = await admin.PostAsJsonAsync($"/api/v1/customers/{customer.Id}/vehicles",
            new SaveVehicleRequest("Kia", "Rio", 2019, "в 456 ор 196", null), Json.Options);
        add.StatusCode.ShouldBe(HttpStatusCode.Created);

        var found = await admin.GetFromJsonAsync<VehicleDto>("/api/v1/vehicles?plate=B456OP196", Json.Options);
        found!.CustomerId.ShouldBe(customer.Id);

        var duplicate = await admin.PostAsJsonAsync($"/api/v1/customers/{customer.Id}/vehicles",
            new SaveVehicleRequest("Kia", "Rio", 2019, "B456OP196", null), Json.Options);
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Customer_card_shows_vehicles_and_visits()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var data = new TestData(Api, admin);
        var (customer, vehicle) = await data.CustomerWithVehicleAsync();
        var bay = await data.BayAsync();
        var service = await data.ServiceAsync();
        await data.BookAsync(TestData.Booking(customer, vehicle, bay, TestData.FutureWorkday(10), service));

        var card = (await admin.GetFromJsonAsync<CustomerCardDto>($"/api/v1/customers/{customer.Id}", Json.Options))!;

        card.Vehicles.Single().Plate.ShouldBe(vehicle.Plate);
        card.TotalVisits.ShouldBe(1);
        card.RecentVisits.Single().Plate.ShouldBe(vehicle.Plate);
    }

    [Fact]
    public async Task Customer_with_upcoming_booking_cannot_be_archived()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var data = new TestData(Api, admin);
        var (customer, vehicle) = await data.CustomerWithVehicleAsync();
        await data.BookAsync(TestData.Booking(customer, vehicle, await data.BayAsync(), TestData.FutureWorkday(10), await data.ServiceAsync()));

        var response = await admin.PostAsync($"/api/v1/customers/{customer.Id}/archive", null);

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Mechanics_have_no_access_to_the_customer_base()
    {
        using var admin = await Api.CreateAdminClientAsync();
        using var mechanic = await new TestData(Api, admin).StaffClientAsync(UserRole.Mechanic);

        (await mechanic.GetAsync("/api/v1/customers")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<CustomerDto> Create(HttpClient client, string name, string phone)
    {
        var response = await client.PostAsJsonAsync("/api/v1/customers", new SaveCustomerRequest(name, phone, null, null), Json.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDto>(Json.Options))!;
    }

    private static async Task<PagedResult<CustomerDto>> SearchAsync(HttpClient client, string term) =>
        (await client.GetFromJsonAsync<PagedResult<CustomerDto>>($"/api/v1/customers?search={Uri.EscapeDataString(term)}", Json.Options))!;
}
