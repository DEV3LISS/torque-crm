using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Torque.Application.Auth;
using Torque.Application.Customers;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests.Security;

/// <summary>Attacks from the OWASP API Security Top 10, tried for real against the running API.</summary>
public class SecurityTests(ApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Responses_carry_defensive_headers()
    {
        using var client = Api.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "torque-crm",
            Audience = "torque-crm-clients",
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["role"] = "Admin" },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("attacker-key-attacker-key-attacker-key-123")), SecurityAlgorithms.HmacSha256),
        });
        using var client = Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        (await client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unsigned_alg_none_token_is_rejected()
    {
        // Classic JWT attack: header {"alg":"none"} and no signature.
        static string B64(string s) => Base64UrlEncoder.Encode(s);
        var token = $"{B64("""{"alg":"none","typ":"JWT"}""")}.{B64($$"""{"sub":"{{Guid.NewGuid()}}","role":"Admin","iss":"torque-crm","aud":"torque-crm-clients","exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}""")}.";
        using var client = Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sql_injection_in_search_is_just_text()
    {
        using var admin = await Api.CreateAdminClientAsync();
        await admin.PostAsJsonAsync("/api/v1/customers", new SaveCustomerRequest("Real Customer", "+79120000077", null, null), Json.Options);

        var response = await admin.GetAsync("/api/v1/customers?search=" + Uri.EscapeDataString("x' OR '1'='1"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"totalCount\":0");
    }

    [Fact]
    public async Task Oversized_body_is_refused()
    {
        using var client = Api.CreateClient();
        var huge = new LoginRequest("a@b.c", new string('x', 2 * 1024 * 1024));

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", huge);

        await response.ShouldBeProblemAsync(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Mass_assignment_cannot_escalate_role_on_create()
    {
        // The request DTO has no "isActive" or "id"; extra JSON fields are ignored, not bound to the entity.
        using var admin = await Api.CreateAdminClientAsync();

        var response = await admin.PostAsync("/api/v1/customers", new StringContent(
            """{"fullName":"Hacker","phone":"+79120000099","isArchived":true,"id":"00000000-0000-0000-0000-000000000001"}""",
            Encoding.UTF8, "application/json"));

        var created = (await response.Content.ReadFromJsonAsync<CustomerDto>(Json.Options))!;
        created.IsArchived.ShouldBeFalse();
        created.Id.ShouldNotBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }
}
