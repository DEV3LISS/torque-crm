using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Formatting.Compact;
using Torque.Api.Setup;
using Torque.Application;
using Torque.Application.Abstractions;
using Torque.Application.Users;
using Torque.Infrastructure;
using Torque.Infrastructure.Persistence;

// Logs written before the host is built (startup failures) still go somewhere.
Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Requests in this API are small JSON documents; refuse anything bigger than 1 MB early
    // (cheap protection against memory-exhaustion requests).
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.Limits.MaxRequestBodySize = RequestSizeLimitMiddleware.MaxBytes;
        k.AddServerHeader = false; // do not advertise "Kestrel"
    });

    builder.Services.AddSerilog((services, logger) =>
    {
        logger.ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        // Humans read logs locally; machines (log collectors) read JSON in production.
        if (builder.Environment.IsDevelopment())
        {
            logger.WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {CorrelationId}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture);
        }
        else
        {
            logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        }
    });

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiAuth(builder.Configuration);
    builder.Services.AddApiDocumentation();
    builder.Services.AddObservability(builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();

    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddProblemDetails(options =>
        options.CustomizeProblemDetails = ctx =>
        {
            // Every error body carries the correlation id, so a bug report can be matched to log lines.
            if (ctx.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string id)
            {
                ctx.ProblemDetails.Extensions["correlationId"] = id;
            }
        });
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
        .AddCheck<OutboxHealthCheck>("outbox", tags: ["status"]);

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

    var app = builder.Build();

    // CLI mode: `dotnet Torque.Api.dll create-admin --email a@b.c --name "Owner"`, password from the
    // ADMIN_PASSWORD environment variable (so it does not end up in shell history).
    if (args.Length > 0 && args[0] == "create-admin")
    {
        return await CreateAdminCommand.RunAsync(app.Services, builder.Configuration);
    }

    await DatabaseInitializer.InitializeAsync(app.Services);

    // API docs are part of the portfolio demo, so they are on by default; a real deployment can turn them off.
    var docsEnabled = app.Configuration.GetValue("ApiDocs:Enabled", true);

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseMiddleware<RequestSizeLimitMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages(); // empty 401/403/404/429 responses also get a ProblemDetails body

    // Static Swagger UI pages are served by middleware, not endpoints; the fallback "must be logged in"
    // policy would block them too, so the UI goes before authentication/authorization.
    if (docsEnabled)
    {
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/openapi/v1.json", "Torque CRM API v1");
            o.RoutePrefix = "swagger";
        });
    }

    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    if (docsEnabled)
    {
        app.MapOpenApi().AllowAnonymous();
    }

    // Liveness: the process is up. Readiness: it can also reach its dependencies (DB).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
    // Full picture for humans/monitoring (DB + outbox), with details as JSON. Admin only — it reveals internals.
    app.MapHealthChecks("/health/status", new HealthCheckOptions { ResponseWriter = HealthResponse.WriteJsonAsync })
        .RequireAuthorization(Policies.AdminOnly);

    // Prometheus scrapes this. In production expose it only on the internal network (reverse proxy rule);
    // here it is anonymous so the demo stack works out of the box.
    app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

    app.MapControllers();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application failed to start");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

internal static class CreateAdminCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration config)
    {
        var email = config["email"];
        var name = config["name"] ?? "Administrator";
        var password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await Console.Error.WriteLineAsync("Usage: ADMIN_PASSWORD=... Torque.Api create-admin --email <email> [--name <name>]");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<UserService>()
            .CreateInitialAdminAsync(email, name, password, CancellationToken.None);
        Console.WriteLine(created ? "Admin created." : "Users already exist — nothing changed.");
        return created ? 0 : 3;
    }
}

/// <summary>Makes the entry point visible to the integration tests (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program;
