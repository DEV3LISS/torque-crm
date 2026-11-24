using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Torque.IntegrationTests.Infrastructure;

public static class ProblemAssertions
{
    /// <summary>Checks the status code and that the body is RFC 9457 ProblemDetails with a correlation id.</summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.TryGetProperty("correlationId", out _).ShouldBeTrue();
        return body;
    }
}
