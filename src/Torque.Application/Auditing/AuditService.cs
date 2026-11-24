using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Domain.Auditing;

namespace Torque.Application.Auditing;

public sealed record AuditLogDto(long Id, DateTimeOffset OccurredAt, Guid? UserId, string? UserName, string EntityType,
    string EntityId, AuditAction Action, string Changes, string? CorrelationId);

public sealed record AuditQuery(PageRequest Paging, string? EntityType = null, string? EntityId = null, Guid? UserId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null);

public sealed class AuditQueryValidator : AbstractValidator<AuditQuery>
{
    public AuditQueryValidator()
    {
        RuleFor(x => x.Paging).ValidPage();
        RuleFor(x => x.EntityType).MaximumLength(100);
        RuleFor(x => x.EntityId).MaximumLength(100);
    }
}

/// <summary>Read side of the audit log ("who gave this discount?"). Writing happens automatically in Infrastructure.</summary>
public sealed class AuditService(IAppDbContext db, IValidator<AuditQuery> validator)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(query, ct);
        var logs = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            logs = logs.Where(l => l.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            logs = logs.Where(l => l.EntityId == query.EntityId);
        }

        if (query.UserId is not null)
        {
            logs = logs.Where(l => l.UserId == query.UserId);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            logs = logs.Where(l => l.OccurredAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            logs = logs.Where(l => l.OccurredAt < toUtc);
        }

        var total = await logs.CountAsync(ct);
        var items = await logs
            .OrderByDescending(l => l.Id)
            .Skip(query.Paging.Skip).Take(query.Paging.PageSize)
            .GroupJoin(db.Users, l => l.UserId, u => u.Id, (l, users) => new { l, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new AuditLogDto(
                x.l.Id, x.l.OccurredAt, x.l.UserId, u == null ? null : u.FullName, x.l.EntityType, x.l.EntityId,
                x.l.Action, x.l.Changes, x.l.CorrelationId))
            .ToListAsync(ct);

        return new PagedResult<AuditLogDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }
}
