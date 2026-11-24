using Torque.Domain.Common;

namespace Torque.Domain.Customers;

/// <summary>
/// Phone numbers arrive in every possible format: "8 (912) 345-67-89", "+7 912 3456789", "9123456789".
/// We store one canonical E.164 form ("+79123456789") so that uniqueness and search work.
/// </summary>
public static class PhoneNumber
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException("Phone number is required.");
        }

        var digits = DigitsOnly(raw);

        // Russian conventions: a leading 8 is the domestic trunk prefix for +7,
        // and a bare 10-digit mobile number (9xx…) is missing its country code.
        if (digits.Length == 11 && digits[0] == '8')
        {
            digits = "7" + digits[1..];
        }
        else if (digits.Length == 10 && digits[0] == '9')
        {
            digits = "7" + digits;
        }

        // E.164 allows at most 15 digits; anything shorter than 11 cannot be a full international number.
        if (digits.Length is < 11 or > 15)
        {
            throw new DomainException($"'{raw}' is not a valid phone number.");
        }

        return "+" + digits;
    }

    public static string DigitsOnly(string raw) => new(raw.Where(char.IsAsciiDigit).ToArray());
}
