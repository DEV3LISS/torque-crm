using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Torque.Domain.Auditing;
using Torque.Domain.Orders;
using Torque.Domain.Auth;
using Torque.Domain.Catalog;
using Torque.Domain.Customers;
using Torque.Domain.Messaging;
using Torque.Domain.Scheduling;
using Torque.Domain.Users;

namespace Torque.Application.Abstractions;

/// <summary>
/// What the use cases need from the database. Implemented by the EF Core DbContext in Infrastructure.
/// </summary>
/// <remarks>
/// We expose DbSet directly instead of hiding EF Core behind repositories (ADR-002):
/// DbContext already is a unit of work + repository, and DbSet keeps projections, AsNoTracking
/// and other EF features available to the use cases.
/// </remarks>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<ServiceOffering> Services { get; }
    DbSet<Bay> Bays { get; }
    DbSet<WorkingHours> WorkingHours { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<WorkOrder> WorkOrders { get; }
    DbSet<Payment> Payments { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the work order with its lines and locks the row until the transaction ends
    /// (SELECT … FOR UPDATE). A second transaction trying to lock the same order waits.
    /// </summary>
    Task<WorkOrder?> LockWorkOrderAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string passwordHash, string password);
}

public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>Correct password, but the hash uses old settings — store a fresh hash.</summary>
    SuccessRehashNeeded,
}

public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAccessToken(User user);

    /// <summary>Cryptographically random string handed to the client. Never stored as is.</summary>
    string GenerateRefreshToken();

    /// <summary>SHA-256 of the refresh token — this is what we store and look up.</summary>
    string HashRefreshToken(string refreshToken);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>The user who sent the current request (from the JWT), or nobody.</summary>
public interface ICurrentUser
{
    Guid? Id { get; }

    UserRole? Role { get; }

    /// <summary>Correlation id of the current request (for the audit log).</summary>
    string? CorrelationId { get; }
}

/// <summary>The business's time zone (opening hours and "today" are local to it).</summary>
public interface IBusinessTimeZone
{
    TimeZoneInfo TimeZone { get; }
}

/// <summary>
/// Delivers a text message to a customer (SMS gateway, Telegram, …). Implementations must be safe to call
/// twice with the same <see cref="Notification.MessageId"/>: the outbox guarantees at-least-once delivery.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record Notification(Guid MessageId, string Phone, string Text);
