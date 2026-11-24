namespace Torque.Api.Setup;

/// <summary>
/// Defensive HTTP headers. This is a JSON API, so the policy is strict: nothing may be framed,
/// sniffed or loaded from this origin as a page — except Swagger UI, which needs its scripts.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";             // browsers must trust Content-Type, not guess
        headers.XFrameOptions = "DENY";                      // no clickjacking via <iframe>
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers.CacheControl = "no-store";               // API responses contain personal data: never cache
        }

        return next(context);
    }
}
