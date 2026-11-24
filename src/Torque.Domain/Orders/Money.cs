namespace Torque.Domain.Orders;

/// <summary>Money helpers. Amounts are decimal (exact), rounded to kopecks with "half away from zero" like a receipt.</summary>
public static class Money
{
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static bool IsValidAmount(decimal amount) => amount > 0 && decimal.Round(amount, 2) == amount;
}
