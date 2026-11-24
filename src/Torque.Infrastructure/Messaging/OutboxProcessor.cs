using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Torque.Application.Common;
using Torque.Application.Messaging;
using Torque.Domain.Messaging;
using Torque.Infrastructure.Persistence;

namespace Torque.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Run the background worker. Off in tests (they call <see cref="OutboxDispatcher"/> directly).</summary>
    public bool Enabled { get; init; } = true;
    public int PollIntervalSeconds { get; init; } = 15;
    public int BatchSize { get; init; } = 20;
}

/// <summary>
/// Picks due outbox messages and hands each to its handler.
/// </summary>
/// <remarks>
/// <b>FOR UPDATE SKIP LOCKED</b>: several API instances can run this worker at once. Each locks the rows it
/// takes; the others skip locked rows instead of waiting — so every message is processed by exactly one worker,
/// with no extra coordination (no Redis, no leader election).
/// <para>Delivery is <b>at-least-once</b>: if the process dies after the SMS went out but before COMMIT, the
/// message is sent again. Hence the handler passes a stable MessageId that the gateway can de-duplicate on.</para>
/// </remarks>
public sealed partial class OutboxDispatcher(
    AppDbContext db,
    IEnumerable<IOutboxMessageHandler> handlers,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    TorqueMetrics metrics,
    ILogger<OutboxDispatcher> logger)
{
    /// <returns>Number of messages processed in this batch.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var batchSize = options.Value.BatchSize;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var messages = await db.Set<OutboxMessage>()
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE status = 'Pending' AND due_at <= {now}
                ORDER BY due_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            var handler = handlers.SingleOrDefault(h => h.Type == message.Type);
            if (handler is null)
            {
                message.MarkFailed($"No handler for message type '{message.Type}'.", now);
                continue;
            }

            try
            {
                var outcome = await handler.HandleAsync(message, ct);
                if (outcome == HandleOutcome.Sent)
                {
                    message.MarkSent(clock.GetUtcNow());
                }
                else
                {
                    message.MarkSkipped(clock.GetUtcNow());
                }

                metrics.OutboxProcessed(message.Type, outcome.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken message must not block the others: record, back off, continue.
                message.MarkFailed(ex.Message, clock.GetUtcNow());
                metrics.OutboxProcessed(message.Type, message.Status == OutboxStatus.Failed ? "Failed" : "Retry");
                LogDeliveryFailed(logger, ex, message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return messages.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed, attempt {Attempt}")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, Guid messageId, string type, int attempt);
}

/// <summary>Hosted background loop. Each iteration gets its own DI scope (fresh DbContext).</summary>
public sealed partial class OutboxWorker(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, ILogger<OutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));
        do
        {
            try
            {
                int processed;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    processed = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().ProcessBatchAsync(stoppingToken);
                }
                while (processed > 0 && !stoppingToken.IsCancellationRequested); // drain backlog, then wait
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The database may be down for a moment — log and try again on the next tick, never crash the host.
                LogLoopFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox worker iteration failed")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);
}
