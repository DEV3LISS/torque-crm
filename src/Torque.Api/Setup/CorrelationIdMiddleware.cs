using System.Diagnostics;
using Serilog.Context;

namespace Torque.Api.Setup;

/// <summary>
/// Gives every request an id that appears in every log line and in the response header / error body.
/// A user reports "error, id 4bf92f…" and we find the exact request in the logs.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!IsSafe(id))
        {
            id = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        }

        context.Items[ItemKey] = id;
        context.Response.Headers[HeaderName] = id;

        using (LogContext.PushProperty(ItemKey, id))
        {
            await next(context);
        }
    }

    // Never trust a client header blindly: limit length and characters (log injection).
    private static bool IsSafe(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
