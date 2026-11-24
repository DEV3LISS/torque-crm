using System.Net;
using Torque.IntegrationTests.Infrastructure;

namespace Torque.IntegrationTests;

public class HealthTests(ApiFactory api) : ApiTest(api)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_public_and_healthy(string url)
    {
        using var client = Api.CreateClient();

        var response = await client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_ui_is_public()
    {
        using var client = Api.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApi_document_is_available()
    {
        using var client = Api.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("/api/v1/auth/login");
    }
}
