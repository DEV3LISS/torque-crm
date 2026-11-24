using Torque.Domain.Common;

namespace Torque.Domain.Catalog;

/// <summary>
/// An item of the service catalog ("Tyre change R15–R16", 30 min, 2000 ₽).
/// Named ServiceOffering to avoid confusion with C# "service" classes; the table is "services".
/// </summary>
public sealed class ServiceOffering : Entity, IHasTimestamps
{
    public const int SlotMinutes = 15;
    public const int MaxDurationMinutes = 8 * 60;
    public const int MaxNameLength = 200;

    private ServiceOffering() { }

    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;

    /// <summary>Multiple of 15 minutes — the booking grid.</summary>
    public int DurationMinutes { get; private set; }

    /// <summary>Current price. Orders copy it at the moment of sale, so changing it never rewrites history.</summary>
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static ServiceOffering Create(string name, string category, int durationMinutes, decimal price)
    {
        var service = new ServiceOffering { IsActive = true };
        service.Update(name, category, durationMinutes, price);
        return service;
    }

    public void Update(string name, string category, int durationMinutes, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
        {
            throw new DomainException("Name and category are required.");
        }

        if (durationMinutes <= 0 || durationMinutes > MaxDurationMinutes || durationMinutes % SlotMinutes != 0)
        {
            throw new DomainException($"Duration must be a multiple of {SlotMinutes} minutes, up to {MaxDurationMinutes}.");
        }

        if (price < 0 || decimal.Round(price, 2) != price)
        {
            throw new DomainException("Price must be non-negative with at most 2 decimal places.");
        }

        Name = name.Trim();
        Category = category.Trim();
        DurationMinutes = durationMinutes;
        Price = price;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
