using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Torque.Application.Common;
using Torque.Domain.Common;
using Torque.Domain.Orders;

namespace Torque.Api.Setup;

/// <summary>
/// The single place where exceptions become HTTP responses (RFC 9457 ProblemDetails).
/// Use cases throw meaningful exception types and never deal with status codes themselves.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException ex => Validation(ex),
            NotFoundException ex => Problem(StatusCodes.Status404NotFound, "Not found", ex.Message),
            ConflictException ex => Problem(StatusCodes.Status409Conflict, "Conflict", ex.Message),
            UnauthorizedException ex => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", ex.Message),
            ForbiddenException ex => Problem(StatusCodes.Status403Forbidden, "Forbidden", ex.Message),
            InvalidStateException ex => Problem(StatusCodes.Status409Conflict, "Invalid state", ex.Message),
            DiscountApprovalRequiredException ex => Problem(StatusCodes.Status403Forbidden, "Approval required", ex.Message),
            DomainException ex => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", ex.Message),
            DbUpdateConcurrencyException => Problem(StatusCodes.Status409Conflict, "Concurrent update",
                "The record was changed by someone else. Reload it and try again."),
            _ => null,
        };

        // Database errors can arrive wrapped more than once (EF's DbUpdateException, and for transient errors
        // such as deadlocks also the execution strategy's InvalidOperationException) — search the whole chain.
        if (problem is null && FindPostgresError(exception) is { } pg)
        {
            problem = FromPostgres(pg);
        }

        // Database unreachable (down, restarting, network blip, pool exhausted): not our bug and not the
        // client's — the service is temporarily unavailable. 503 + Retry-After tells clients and load balancers
        // to try again shortly instead of reporting a server error.
        if (problem is null && IsDatabaseUnavailable(exception))
        {
            LogDatabaseUnavailable(logger, exception.GetBaseException().Message);
            httpContext.Response.Headers.RetryAfter = "5";
            problem = Problem(StatusCodes.Status503ServiceUnavailable, "Service temporarily unavailable",
                "The database is not reachable right now. Please retry in a few seconds.");
        }

        if (problem is null)
        {
            // Unknown error = a bug. Full details go to the log; the client only gets a correlation id
            // (added in Program.cs) to quote when reporting the problem.
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
            problem = Problem(StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ValidationProblemDetails Validation(ValidationException ex)
    {
        var errors = ex.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
        };
    }

    private static bool IsDatabaseUnavailable(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is NpgsqlException { IsTransient: true } and not PostgresException
                or System.Net.Sockets.SocketException
                or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static PostgresException? FindPostgresError(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is PostgresException pg)
            {
                return pg;
            }
        }

        return null;
    }

    /// <summary>Database constraints are the last line of defence; translate their violations into 409s.</summary>
    private static ProblemDetails? FromPostgres(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => Problem(StatusCodes.Status409Conflict, "Duplicate",
            "A record with the same unique value already exists."),
        PostgresErrorCodes.ExclusionViolation => Problem(StatusCodes.Status409Conflict, "Time slot taken",
            "The time slot overlaps with an existing booking."),
        PostgresErrorCodes.ForeignKeyViolation => Problem(StatusCodes.Status409Conflict, "Related record conflict",
            "The operation conflicts with related records."),
        // Several transactions competing for the same rows (e.g. overlapping bookings inserted at the same
        // moment) can deadlock; PostgreSQL aborts one of them. The victim was competing for something another
        // request got — a conflict, not a server bug. Safe for the client to retry.
        PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure =>
            Problem(StatusCodes.Status409Conflict, "Concurrent conflict", "Another request changed the same data at the same time. Please retry."),
        _ => null,
    };

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable: {Reason}")]
    private static partial void LogDatabaseUnavailable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
