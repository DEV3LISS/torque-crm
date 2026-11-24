namespace Torque.Application.Common;

// Application-level errors. The API layer maps each type to an HTTP status code in one place
// (GlobalExceptionHandler), so use cases never think about HTTP.

/// <summary>HTTP 404.</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

/// <summary>HTTP 409 — e.g. duplicate email, slot already taken.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>HTTP 401 — not logged in or credentials/token are invalid.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);

/// <summary>HTTP 403 — logged in, but this action is not allowed for you.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
