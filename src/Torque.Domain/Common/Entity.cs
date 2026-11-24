namespace Torque.Domain.Common;

/// <summary>
/// Base class for everything that has an identity and lives in its own table.
/// </summary>
/// <remarks>
/// Ids are UUID version 7: generated in the application (no round-trip to the DB to learn the id),
/// impossible to guess in a URL, and time-ordered so B-tree index inserts stay at the "end" of the index
/// instead of fragmenting it like random UUID v4 would.
/// </remarks>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}

/// <summary>
/// Entities that track when they were created and last changed.
/// The values are filled in automatically by an EF Core interceptor, not by hand.
/// </summary>
public interface IHasTimestamps
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
