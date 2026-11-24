namespace Torque.Domain.Common;

/// <summary>
/// A business rule was violated, e.g. "cannot book in the past".
/// The API turns it into HTTP 422 Unprocessable Content.
/// </summary>
public class DomainException(string message) : Exception(message);

/// <summary>
/// The action is not allowed in the entity's current state, e.g. "cannot complete a cancelled appointment".
/// The API turns it into HTTP 409 Conflict.
/// </summary>
public sealed class InvalidStateException(string message) : DomainException(message);
