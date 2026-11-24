using Torque.Domain.Common;

namespace Torque.Domain.Customers;

/// <summary>
/// The car the work is done on. History ("what did we do last season?") is per vehicle.
/// </summary>
public sealed class Vehicle : Entity, IHasTimestamps
{
    public const int MaxMakeModelLength = 100;

    private Vehicle() { }

    public Guid CustomerId { get; private set; }
    public string Make { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public int? Year { get; private set; }

    /// <summary>Normalised licence plate (see <see cref="LicensePlate"/>). Unique.</summary>
    public string Plate { get; private set; } = null!;
    public string? Vin { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Vehicle Create(Guid customerId, string make, string model, int? year, string plate, string? vin, int currentYear)
    {
        var vehicle = new Vehicle { CustomerId = customerId };
        vehicle.Update(make, model, year, plate, vin, currentYear);
        return vehicle;
    }

    public void Update(string make, string model, int? year, string plate, string? vin, int currentYear)
    {
        if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model))
        {
            throw new DomainException("Make and model are required.");
        }

        if (year is not null && (year < 1950 || year > currentYear + 1))
        {
            throw new DomainException($"Year must be between 1950 and {currentYear + 1}.");
        }

        Make = make.Trim();
        Model = model.Trim();
        Year = year;
        Plate = LicensePlate.Normalize(plate);
        Vin = string.IsNullOrWhiteSpace(vin) ? null : NormalizeVin(vin);
    }

    /// <summary>VIN: exactly 17 characters, letters I, O and Q are never used (confusable with 1 and 0).</summary>
    private static string NormalizeVin(string vin)
    {
        var value = vin.Trim().ToUpperInvariant();
        if (value.Length != 17 || !value.All(char.IsAsciiLetterOrDigit) || value.IndexOfAny(['I', 'O', 'Q']) >= 0)
        {
            throw new DomainException($"'{vin}' is not a valid VIN.");
        }

        return value;
    }
}

/// <summary>
/// Russian plates use only the 12 Cyrillic letters that look like Latin ones (А В Е К М Н О Р С Т У Х).
/// Front-desk staff type them in either keyboard layout, so "А123ВС96" (Cyrillic) and "A123BC96" (Latin)
/// must be the same plate. We store the Latin form without spaces.
/// </summary>
public static class LicensePlate
{
    private static readonly Dictionary<char, char> CyrillicToLatin = new()
    {
        ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['К'] = 'K', ['М'] = 'M', ['Н'] = 'H',
        ['О'] = 'O', ['Р'] = 'P', ['С'] = 'C', ['Т'] = 'T', ['У'] = 'Y', ['Х'] = 'X',
    };

    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException("Licence plate is required.");
        }

        var chars = raw.ToUpperInvariant()
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(c => CyrillicToLatin.TryGetValue(c, out var latin) ? latin : c)
            .ToArray();
        var plate = new string(chars);

        if (plate.Length is < 4 or > 12 || !plate.All(char.IsAsciiLetterOrDigit))
        {
            throw new DomainException($"'{raw}' is not a valid licence plate.");
        }

        return plate;
    }
}
