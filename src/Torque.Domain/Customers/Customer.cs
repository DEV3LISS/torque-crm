using Torque.Domain.Common;

namespace Torque.Domain.Customers;

public sealed class Customer : Entity, IHasTimestamps
{
    public const int MaxNameLength = 200;
    public const int MaxNotesLength = 2000;

    private Customer() { }

    public string FullName { get; private set; } = null!;

    /// <summary>E.164, e.g. +79123456789. Unique.</summary>
    public string Phone { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Archived instead of deleted: their visits, orders and payments must stay in history.</summary>
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Customer Create(string fullName, string phone, string? email, string? notes)
    {
        var customer = new Customer();
        customer.Update(fullName, phone, email, notes);
        return customer;
    }

    public void Update(string fullName, string phone, string? email, string? notes)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Customer name is required.");
        }

        FullName = fullName.Trim();
        Phone = PhoneNumber.Normalize(phone);
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void Archive()
    {
        if (IsArchived)
        {
            throw new InvalidStateException("Customer is already archived.");
        }

        IsArchived = true;
    }

    public void Restore()
    {
        if (!IsArchived)
        {
            throw new InvalidStateException("Customer is not archived.");
        }

        IsArchived = false;
    }
}
