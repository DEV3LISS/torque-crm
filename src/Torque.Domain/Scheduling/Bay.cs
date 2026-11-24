using Torque.Domain.Common;

namespace Torque.Domain.Scheduling;

/// <summary>A physical service bay / lift. Double-booking is defined per bay.</summary>
public sealed class Bay : Entity, IHasTimestamps
{
    public const int MaxNameLength = 100;

    private Bay() { }

    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Bay Create(string name)
    {
        var bay = new Bay { IsActive = true };
        bay.Rename(name);
        return bay;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Bay name is required.");
        }

        Name = name.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
