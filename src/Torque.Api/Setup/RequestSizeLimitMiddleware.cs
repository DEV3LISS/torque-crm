using Microsoft.AspNetCore.Mvc;

namespace Torque.Api.Setup;

/// <summary>
/// Refuses bodies above the limit before any of them is read. Kestrel has its own limit
/// (Program.cs) for streamed bodies, but it does not apply in every hosting model — this check does.
/// </summary>
public sealed class RequestSizeLimitMiddleware(RequestDelegate next)
{
    public const long MaxBytes = 1024 * 1024;

    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problems)
    {
        if (context.Request.ContentLength > MaxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status413PayloadTooLarge,
                    Title = "Request too large",
                    Detail = $"The request body must not exceed {MaxBytes / 1024} KB.",
                },
            });
            return;
        }

        await next(context);
    }
}
