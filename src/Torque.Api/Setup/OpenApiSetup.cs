using Microsoft.OpenApi;

namespace Torque.Api.Setup;

public static class OpenApiSetup
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Torque CRM API",
                    Version = "v1",
                    Description = "Backend for a car service / tyre shop: customers, vehicles, bookings without double-booking, work orders, payments, audit.",
                };

                // Adds the "Authorize" button in Swagger UI: paste the access token from /auth/login.
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token from POST /api/v1/auth/login",
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] },
                ];
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
