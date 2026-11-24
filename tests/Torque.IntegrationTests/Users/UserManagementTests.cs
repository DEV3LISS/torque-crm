using System.Net;
using System.Net.Http.Json;
using Torque.Application.Auth;
using Torque.Application.Common;
using Torque.Application.Users;
using Torque.Domain.Users;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Users;

public class UserManagementTests(ApiFactory api) : ApiTest(api)
{
    private const string Password = "Manager-Password-1";

    [Fact]
    public async Task Admin_creates_a_user_who_can_then_log_in()
    {
        using var admin = await Api.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("Manager@Test.Local", "Maria Manager", Password, UserRole.Manager), Json.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
        var created = (await response.Content.ReadFromJsonAsync<UserDto>(Json.Options))!;
        created.Email.ShouldBe("manager@test.local");
        created.Role.ShouldBe(UserRole.Manager);

        using var manager = await Api.CreateClientAsAsync("manager@test.local", Password);
        var me = (await manager.GetFromJsonAsync<UserDto>("/api/v1/users/me", Json.Options))!;
        me.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Duplicate_email_returns_409()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var request = new CreateUserRequest("dup@test.local", "First", Password, UserRole.Mechanic);
        (await admin.PostAsJsonAsync("/api/v1/users", request, Json.Options)).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync("/api/v1/users", request with { Email = "DUP@test.local" }, Json.Options);

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        using var admin = await Api.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("weak@test.local", "Weak", "short", UserRole.Mechanic), Json.Options);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("password", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Manager_cannot_manage_users()
    {
        await CreateUser("manager@test.local", UserRole.Manager);
        using var manager = await Api.CreateClientAsAsync("manager@test.local", Password);

        var response = await manager.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("x@test.local", "X", Password, UserRole.Admin), Json.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in_or_refresh()
    {
        var mechanic = await CreateUser("mech@test.local", UserRole.Mechanic);
        var session = await Api.LoginAsync("mech@test.local", Password);
        using var admin = await Api.CreateAdminClientAsync();

        (await admin.PostAsync($"/api/v1/users/{mechanic.Id}/deactivate", null)).EnsureSuccessStatusCode();

        using var anon = Api.CreateClient();
        (await anon.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("mech@test.local", Password)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(session.RefreshToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var me = (await admin.GetFromJsonAsync<UserDto>("/api/v1/users/me", Json.Options))!;

        var response = await admin.PostAsync($"/api/v1/users/{me.Id}/deactivate", null);

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Last_admin_cannot_be_demoted()
    {
        using var admin = await Api.CreateAdminClientAsync();
        var me = (await admin.GetFromJsonAsync<UserDto>("/api/v1/users/me", Json.Options))!;

        var response = await admin.PutAsJsonAsync($"/api/v1/users/{me.Id}/role", new ChangeRoleRequest(UserRole.Manager), Json.Options);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict);
        problem.GetProperty("detail").GetString()!.ShouldContain("At least one active Admin");
    }

    [Fact]
    public async Task List_is_paged_and_hides_inactive_by_default()
    {
        var inactive = await CreateUser("old@test.local", UserRole.Mechanic);
        await CreateUser("new@test.local", UserRole.Mechanic);
        using var admin = await Api.CreateAdminClientAsync();
        (await admin.PostAsync($"/api/v1/users/{inactive.Id}/deactivate", null)).EnsureSuccessStatusCode();

        var active = (await admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=1", Json.Options))!;
        var all = (await admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/users?includeInactive=true", Json.Options))!;

        active.TotalCount.ShouldBe(2); // admin + new
        active.Items.Count.ShouldBe(1);
        active.TotalPages.ShouldBe(2);
        all.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Unknown_user_returns_404()
    {
        using var admin = await Api.CreateAdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound);
    }

    private async Task<UserDto> CreateUser(string email, UserRole role)
    {
        using var admin = await Api.CreateAdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(email, email, Password, role), Json.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Json.Options))!;
    }
}
